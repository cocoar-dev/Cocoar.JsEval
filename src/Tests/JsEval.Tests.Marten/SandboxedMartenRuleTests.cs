using System.Linq.Expressions;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Jint.Native;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Marten;

/// <summary>
/// The combination the library exists for and that nothing else covers: an
/// untrusted rule, executed on a <c>Sandboxed()</c> engine behind an
/// <c>AllowOnly</c> surface, translated into an expression tree and handed to a
/// *real* LINQ provider against a *real* PostgreSQL.
///
/// The in-memory rule-translation tests prove the translation; the provider
/// experiments prove the SQL. Only this suite proves they hold together while
/// the engine is locked down.
///
/// Skips when no database answers — see <see cref="MartenTestStore"/>.
/// </summary>
public class SandboxedMartenRuleTests : IAsyncLifetime
{
    public static bool DatabaseAvailable => MartenTestStore.IsAvailable;

    private DocumentStore? _store;

    public async ValueTask InitializeAsync()
    {
        if (!DatabaseAvailable) return;
        _store = await MartenTestStore.CreateSeeded();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The capability the host grants. It owns the session, pins the tenant
    /// filter, projects to a DTO, and records the SQL that actually went to
    /// PostgreSQL so a test can compare it against a C# baseline.
    /// </summary>
    public sealed class DirectoryModule : IJsModule
    {
        private readonly IQuerySession _session;

        public DirectoryModule(IQuerySession session) => _session = session;

        public string? LastSql { get; private set; }
        public Expression<Func<User, bool>>? LastPredicate { get; private set; }

        /// <summary>Host-controlled lookup: the key is a closed set, not a free path.</summary>
        public string Setting(string key) => key switch
        {
            "tenantDepartment" => "sales",
            _ => throw new ArgumentException($"unknown setting '{key}'")
        };

        /// <summary>
        /// Returns a <see cref="Task{T}"/>, so the script awaits it. Marten 9
        /// permits asynchronous data access only — a synchronous
        /// <c>ToList()</c> here throws <c>NotSupportedException</c>.
        /// </summary>
        public async Task<IReadOnlyList<UserDto>> Find(JsValue rule)
        {
            var jintEngine = (rule as Jint.Native.Object.ObjectInstance)?.Engine;
            var predicate = JsExpressionTranslator.Translate<User, bool>(rule, jintEngine, new TranslationOptions
            {
                IdentifierResolver = name => name == "directory" ? this : null
            });
            LastPredicate = predicate;

            var query = _session.Query<User>()
                .Where(u => u.IsActive)   // host-pinned, not rule-controlled
                .Where(predicate);

            LastSql = query.ToCommand().CommandText;

            return await query
                .Select(u => new UserDto { Name = u.Name, Department = u.Department })
                .ToListAsync();
        }
    }

    /// <summary>
    /// The arrangement an untrusted rule runs under. <c>AllowOnly</c> lists the
    /// DTO's members and nothing else — neither the entity nor Marten's session
    /// appears, so nothing reachable from them exists for the script.
    /// </summary>
    private (JsEngine Engine, IQuerySession Session) CreateEngine()
    {
        var session = _store!.LightweightSession();

        var sc = new ServiceCollection();
        sc.AddJsEval(b => b
            .Sandboxed()
            .AddModule<DirectoryModule>()
            .AllowOnly(a => a
                .Member((UserDto d) => d.Name)
                .Member((UserDto d) => d.Department)));

        var engine = sc.BuildServiceProvider().GetRequiredService<JsEngine>();
        engine.AddModuleParameterInstance(typeof(IQuerySession), () => session);
        return (engine, session);
    }

    private static DirectoryModule Module(JsEngine engine) =>
        engine.GetModuleState<DirectoryModule>()
        ?? throw new InvalidOperationException("module was never instantiated");

    // ---------------------------------------------------------------------
    // The filter reaches the database
    // ---------------------------------------------------------------------

    [Fact(SkipUnless = nameof(DatabaseAvailable), Skip = "requires a local PostgreSQL")]
    public async Task SandboxedRule_ProducesTheSameSqlAsAnEquivalentCSharpLambda()
    {
        var (engine, session) = CreateEngine();
        using (engine)
        await using (session)
        {
            // What Marten produces for the hand-written equivalent.
            Expression<Func<User, bool>> baseline = u => u.Department == "eng" && u.Age >= 18;
            var baselineSql = session.Query<User>()
                .Where(u => u.IsActive)
                .Where(baseline)
                .ToCommand().CommandText;

            await engine.ExecuteAsync("""
                import * as directory from 'directory';
                const matches = await directory.Find(u => u.Department === 'eng' && u.Age >= 18);
                export const names = matches.map(m => m.Name);
                """);

            Assert.Equal(baselineSql, Module(engine).LastSql);
            Assert.Equal(["Alice"], engine.GetValue<string[]>("names")!);
        }
    }

    [Fact(SkipUnless = nameof(DatabaseAvailable), Skip = "requires a local PostgreSQL")]
    public async Task TheFilterRunsInPostgres_NotInMemory()
    {
        var (engine, session) = CreateEngine();
        using (engine)
        await using (session)
        {
            await engine.ExecuteAsync("""
                import * as directory from 'directory';
                export const names = (await directory.Find(u => u.Age > 40)).map(m => m.Name);
                """);

            var sql = Module(engine).LastSql!;

            // The predicate is in the WHERE clause the database received, so the
            // rows never crossed the wire — that is the whole point of
            // translating instead of executing.
            Assert.Contains("where", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Age", sql, StringComparison.Ordinal);
            Assert.Equal(["Cara"], engine.GetValue<string[]>("names")!);
        }
    }

    [Fact(SkipUnless = nameof(DatabaseAvailable), Skip = "requires a local PostgreSQL")]
    public async Task HostPinnedFilter_SurvivesIntoTheSql()
    {
        var (engine, session) = CreateEngine();
        using (engine)
        await using (session)
        {
            // Dan is 50 and in eng, but inactive. The module's own filter wins,
            // and it does so inside the query rather than afterwards.
            await engine.ExecuteAsync("""
                import * as directory from 'directory';
                export const names = (await directory.Find(u => u.Department === 'eng')).map(m => m.Name);
                """);

            Assert.Equal(["Alice", "Bob"], engine.GetValue<string[]>("names")!.OrderBy(n => n).ToArray());
            Assert.Contains("IsActive", Module(engine).LastSql!, StringComparison.Ordinal);
        }
    }

    [Fact(SkipUnless = nameof(DatabaseAvailable), Skip = "requires a local PostgreSQL")]
    public async Task ModuleCallInARule_IsFoldedBeforeTheQueryIsBuilt()
    {
        var (engine, session) = CreateEngine();
        using (engine)
        await using (session)
        {
            await engine.ExecuteAsync("""
                import * as directory from 'directory';
                const matches = await directory.Find(u => u.Department === directory.Setting('tenantDepartment'));
                export const names = matches.map(m => m.Name);
                """);

            Assert.Equal(["Cara"], engine.GetValue<string[]>("names")!);

            // The provider saw the resolved value, not a call it would have had
            // to run per row.
            Assert.Equal("u => (u.Department == \"sales\")", Module(engine).LastPredicate?.ToString());
        }
    }

    // ---------------------------------------------------------------------
    // What the rule author cannot do
    // ---------------------------------------------------------------------

    [Fact(SkipUnless = nameof(DatabaseAvailable), Skip = "requires a local PostgreSQL")]
    public async Task ResultCarriesTheDto_NotTheEntity()
    {
        var (engine, session) = CreateEngine();
        using (engine)
        await using (session)
        {
            await engine.ExecuteAsync("""
                import * as directory from 'directory';
                const first = (await directory.Find(u => u.Age > 18))[0];
                export const probe = first.Name + '|' + typeof first.PasswordHash + '|' + typeof first.Id;
                """);

            // PasswordHash is in Postgres and in the entity, but the projection
            // and the allowlist keep it out of the script's reach.
            Assert.Equal("Alice|undefined|undefined", engine.GetValue<string>("probe"));
        }
    }

    [Fact(SkipUnless = nameof(DatabaseAvailable), Skip = "requires a local PostgreSQL")]
    public async Task RuleCannotReachTheSessionOrTheStore()
    {
        var (engine, session) = CreateEngine();
        using (engine)
        await using (session)
        {
            // The module holds an IQuerySession. It is exported as functions,
            // so the instance itself never enters the engine.
            await engine.ExecuteAsync("""
                import * as directory from 'directory';
                const exported = Object.keys(directory).sort().join(',');
                if (exported !== 'Find,Setting') throw new Error('unexpected exports: ' + exported);
                for (const name of ['fetch', 'require', 'NewObject', 'console', 'Atomics']) {
                    if (typeof globalThis[name] !== 'undefined') throw new Error(name + ' is reachable');
                }
                """);
        }
    }

    [Fact(SkipUnless = nameof(DatabaseAvailable), Skip = "requires a local PostgreSQL")]
    public async Task ModuleValidation_StopsTheQueryBeforeItRuns()
    {
        var (engine, session) = CreateEngine();
        using (engine)
        await using (session)
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => engine.ExecuteAsync("""
                import * as directory from 'directory';
                export const never = await directory.Find(u => u.Department === directory.Setting('../../etc/passwd'));
                """));

            Assert.Contains("unknown setting", Flatten(ex), StringComparison.OrdinalIgnoreCase);
            Assert.Null(Module(engine).LastSql);
        }
    }

    /// <summary>
    /// Pins a documented boundary rather than a guarantee: the translator
    /// resolves members on the *entity* by reflection, so a rule can name any
    /// public property of <see cref="User"/> — including
    /// <c>PasswordHash</c> — and it reaches the SQL. `AllowOnly` does not stop
    /// this, because no CLR member access happens in JavaScript at all. This is
    /// exactly why an untrusted rule must query a DTO, and why this test exists
    /// as documentation instead of being quietly absent.
    /// </summary>
    [Fact(SkipUnless = nameof(DatabaseAvailable), Skip = "requires a local PostgreSQL")]
    public async Task KnownBoundary_ARuleCanStillNameAnyEntityProperty()
    {
        var (engine, session) = CreateEngine();
        using (engine)
        await using (session)
        {
            await engine.ExecuteAsync("""
                import * as directory from 'directory';
                export const names = (await directory.Find(u => u.PasswordHash === 'top-secret')).map(m => m.Name);
                """);

            // The rule could not *read* the value, but it could filter on it.
            Assert.Contains("PasswordHash", Module(engine).LastSql!, StringComparison.Ordinal);
            Assert.NotEmpty(engine.GetValue<string[]>("names")!);
        }
    }

    private static string Flatten(Exception ex)
    {
        var text = new System.Text.StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
            text.Append(e.Message).Append(" | ");
        return text.ToString();
    }
}
