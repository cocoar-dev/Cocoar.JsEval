using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Jint.Native;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Linq;

/// <summary>
/// Authorization rules (RBAC/ABAC) translated to expression trees from inside a
/// <c>Sandboxed()</c> engine, so the filter runs in the database rather than in
/// memory. The rule is never executed as JavaScript — only its AST is mapped —
/// and <c>AllowOnly</c> decides which members the result may expose. The module
/// decides which entity is queryable, which tenant filter is applied, and which
/// shape comes back.
/// </summary>
public class SandboxedRuleTranslationTests
{
    public sealed class User
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string Department { get; set; } = "";
        public bool IsActive { get; set; }
        public string PasswordHash { get; set; } = "top-secret";
    }

    public sealed class UserDto
    {
        public string Name { get; set; } = "";
        public string Department { get; set; } = "";
    }

    /// <summary>
    /// The capability the host grants. It owns the queryable, pins the tenant
    /// filter and the result cap, and projects to a DTO — the rule only ever
    /// contributes a predicate.
    /// </summary>
    public sealed class DirectoryModule : IJsModule
    {
        private readonly IQueryable<User> _users;
        public Expression<Func<User, bool>>? LastPredicate { get; private set; }

        public DirectoryModule(IQueryable<User> users) => _users = users;

        /// <summary>Host-controlled lookup: the key is an enum, not a free path.</summary>
        public string Setting(string key) => key switch
        {
            "tenantDepartment" => "sales",
            "serviceAccount"   => "svc-directory",
            _ => throw new ArgumentException($"unknown setting '{key}'")
        };

        public List<UserDto> Find(JsValue rule)
        {
            // Free identifiers resolve against the engine the rule came from.
            var engine = (rule as Jint.Native.Object.ObjectInstance)?.Engine;
            var options = new TranslationOptions
            {
                // Makes the module itself reachable from inside a rule. Its
                // binding is module-scoped, so the engine lookup cannot see it.
                IdentifierResolver = name => name == "directory" ? this : null
            };
            var predicate = JsExpressionTranslator.Translate<User, bool>(rule, engine, options);
            LastPredicate = predicate;

            return _users
                .Where(u => u.IsActive)   // host-pinned filter, not rule-controlled
                .Where(predicate)
                .Take(50)
                .Select(u => new UserDto { Name = u.Name, Department = u.Department })
                .ToList();
        }
    }

    private static readonly User[] Directory =
    [
        new() { Name = "Alice", Age = 34, Department = "eng",   IsActive = true },
        new() { Name = "Bob",   Age = 17, Department = "eng",   IsActive = true },
        new() { Name = "Cara",  Age = 41, Department = "sales", IsActive = true },
        new() { Name = "Dan",   Age = 50, Department = "eng",   IsActive = false },
    ];

    /// <summary>
    /// The arrangement an untrusted rule runs under: the runtime is locked down
    /// by <c>Sandboxed()</c> and the reachable member surface is exactly the DTO
    /// the module projects to — the entity behind it is never in the allowlist.
    /// </summary>
    private static JsEngine CreateEngine()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b
            .Sandboxed()
            .AddModule<DirectoryModule>()
            .AllowOnly(a => a
                .Member((UserDto d) => d.Name)
                .Member((UserDto d) => d.Department)));

        var engine = sc.BuildServiceProvider().GetRequiredService<JsEngine>();
        engine.AddModuleParameterInstance(typeof(IQueryable<User>), () => Directory.AsQueryable());
        return engine;
    }

    private static DirectoryModule Module(JsEngine engine) =>
        engine.GetModuleState<DirectoryModule>()
        ?? throw new InvalidOperationException("module was never instantiated");

    [Fact]
    public async Task Rule_IsTranslatedToAnExpressionTreeAndFiltersInTheQuery()
    {
        using var engine = CreateEngine();

        await engine.ExecuteAsync("""
            import * as directory from 'directory';
            const matches = directory.Find(u => u.Department === 'eng' && u.Age >= 18);
            export const names = matches.map(m => m.Name);
            """);

        Assert.Equal(["Alice"], engine.GetValue<string[]>("names")!);

        // What reached the provider is a real expression tree, not a delegate.
        Assert.Equal(
            "u => ((u.Department == \"eng\") AndAlso (u.Age >= 18))",
            Module(engine).LastPredicate?.ToString());
    }

    [Fact]
    public async Task TranslatedRule_SupportsTheUsualOperators()
    {
        using var engine = CreateEngine();

        await engine.ExecuteAsync("""
            import * as directory from 'directory';
            export const names = directory
                .Find(u => u.Name.startsWith('C') || u.Age > 40)
                .map(m => m.Name);
            """);

        Assert.Equal(["Cara"], engine.GetValue<string[]>("names")!);
    }

    [Fact]
    public async Task HostPinnedFilters_CannotBeBypassedByTheRule()
    {
        // Dan matches the rule but is inactive; the module's own filter wins.
        using var engine = CreateEngine();

        await engine.ExecuteAsync("""
            import * as directory from 'directory';
            export const names = directory.Find(u => u.Department === 'eng').map(m => m.Name);
            """);

        Assert.Equal(["Alice", "Bob"], engine.GetValue<string[]>("names")!);
    }

    [Fact]
    public async Task ResultShape_IsTheModulesDto_NotTheEntity()
    {
        using var engine = CreateEngine();

        await engine.ExecuteAsync("""
            import * as directory from 'directory';
            const first = directory.Find(u => u.Age > 18)[0];
            export const probe = first.Name + '|' + typeof first.PasswordHash;
            """);

        // The entity's PasswordHash never leaves the host — the projection did.
        Assert.Equal("Alice|undefined", engine.GetValue<string>("probe"));
    }

    [Fact]
    public async Task RuleCannotCaptureAnythingAmbient()
    {
        // A free identifier the resolver does not know fails translation instead
        // of silently resolving to whatever happens to be in scope.
        using var engine = CreateEngine();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => engine.ExecuteAsync("""
            import * as directory from 'directory';
            directory.Find(u => u.Name === somethingAmbient);
            """));

        Assert.Contains("Unresolved identifier", Flatten(ex), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EngineValue_CanParameterizeARule_AndArrivesAsAConstant()
    {
        // The realistic ABAC shape: the host supplies the subject's attributes
        // as values and the rule refers to them by name.
        using var engine = CreateEngine();
        engine.SetValue("allowedDepartment", "sales");
        engine.SetValue("minimumAge", 18);

        await engine.ExecuteAsync("""
            import * as directory from 'directory';
            export const names = directory
                .Find(u => u.Department === allowedDepartment && u.Age >= minimumAge)
                .map(m => m.Name);
            """);

        Assert.Equal(["Cara"], engine.GetValue<string[]>("names")!);

        // The value is baked in as a literal, not as a call on a host object.
        Assert.Equal(
            "u => ((u.Department == \"sales\") AndAlso (u.Age >= 18))",
            Module(engine).LastPredicate?.ToString());
    }

    [Fact]
    public async Task ModuleCallInsideARule_IsEvaluatedOnceAndFoldedToAConstant()
    {
        // The shape a real policy needs: ask the host something mid-rule. The
        // call does not depend on the row, so it is answered during translation
        // and the provider only ever sees the result.
        using var engine = CreateEngine();

        await engine.ExecuteAsync("""
            import * as directory from 'directory';
            export const names = directory
                .Find(u => u.Department === directory.Setting('tenantDepartment'))
                .map(m => m.Name);
            """);

        Assert.Equal(["Cara"], engine.GetValue<string[]>("names")!);
        Assert.Equal("u => (u.Department == \"sales\")", Module(engine).LastPredicate?.ToString());
    }

    [Fact]
    public async Task ModuleCallInARule_SurfacesItsOwnValidation()
    {
        // The module decides which keys exist; an unknown one fails loudly at
        // translation time rather than producing a silently wrong query.
        using var engine = CreateEngine();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => engine.ExecuteAsync("""
            import * as directory from 'directory';
            directory.Find(u => u.Department === directory.Setting('../../etc/passwd'));
            """));

        Assert.Contains("unknown setting", Flatten(ex), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SandboxGuaranteesStillHold_WhileRulesAreTranslated()
    {
        // Translating rules does not reopen anything Sandboxed() closed, and the
        // allowlist still governs what a returned object exposes.
        using var engine = CreateEngine();

        await engine.ExecuteAsync("""
            import * as directory from 'directory';
            const forbidden = ['fetch', 'require', 'NewObject', 'console', 'setTimeout',
                               'Atomics', 'SharedArrayBuffer'];
            for (const name of forbidden) {
                if (typeof globalThis[name] !== 'undefined') throw new Error(name + ' is reachable');
            }
            const r = directory.Find(u => u.Age > 0)[0];
            if (typeof r.GetType !== 'undefined') throw new Error('CLR members leaked');
            """);
    }

    /// <summary>
    /// A script failure crosses several layers (Jint, the module bridge, the
    /// translator), so the reason can sit on an inner exception.
    /// </summary>
    private static string Flatten(Exception ex)
    {
        var text = new System.Text.StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
            text.Append(e.Message).Append(" | ");
        return text.ToString();
    }
}
