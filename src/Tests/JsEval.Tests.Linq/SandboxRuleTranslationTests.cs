using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Jint.Native;
using Xunit;

namespace JsEval.Tests.Linq;

/// <summary>
/// Authorization rules (RBAC/ABAC) translated to expression trees from inside
/// <see cref="JsSandbox"/>, so the filter runs in the database rather than in
/// memory. The rule is never executed as JavaScript — only its AST is mapped —
/// and the sandbox holds no host globals, so a rule cannot capture anything the
/// module did not hand it. The module decides which entity is queryable, which
/// tenant filter is applied, and which shape comes back.
/// </summary>
public class SandboxRuleTranslationTests
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
            // Inside the sandbox that is safe by construction: every global
            // there is a JSON value the host supplied, so a resolved identifier
            // becomes a ConstantExpression and can never be a live host object.
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

    private static JsSandbox CreateSandbox(out DirectoryModule module)
    {
        var sandbox = new JsSandbox();
        module = new DirectoryModule(Directory.AsQueryable());
        sandbox.AddModule("directory", module);
        return sandbox;
    }

    [Fact]
    public async Task Rule_IsTranslatedToAnExpressionTreeAndFiltersInTheQuery()
    {
        var sandbox = CreateSandbox(out var module);
        sandbox.SetValue("names", Array.Empty<string>());

        await sandbox.ExecuteAsync("""
            import * as directory from 'directory';
            const matches = directory.Find(u => u.Department === 'eng' && u.Age >= 18);
            names = matches.map(m => m.Name);
            """);

        Assert.Equal(["Alice"], sandbox.GetValue<string[]>("names")!);

        // What reached the provider is a real expression tree, not a delegate.
        Assert.Equal(
            "u => ((u.Department == \"eng\") AndAlso (u.Age >= 18))",
            module.LastPredicate?.ToString());
    }

    [Fact]
    public async Task TranslatedRule_SupportsTheUsualOperators()
    {
        var sandbox = CreateSandbox(out _);
        sandbox.SetValue("names", Array.Empty<string>());

        await sandbox.ExecuteAsync("""
            import * as directory from 'directory';
            names = directory
                .Find(u => u.Name.startsWith('C') || u.Age > 40)
                .map(m => m.Name);
            """);

        Assert.Equal(["Cara"], sandbox.GetValue<string[]>("names")!);
    }

    [Fact]
    public async Task HostPinnedFilters_CannotBeBypassedByTheRule()
    {
        // Dan matches the rule but is inactive; the module's own filter wins.
        var sandbox = CreateSandbox(out _);
        sandbox.SetValue("names", Array.Empty<string>());

        await sandbox.ExecuteAsync("""
            import * as directory from 'directory';
            names = directory.Find(u => u.Department === 'eng').map(m => m.Name);
            """);

        Assert.Equal(["Alice", "Bob"], sandbox.GetValue<string[]>("names")!);
    }

    [Fact]
    public async Task ResultShape_IsTheModulesDto_NotTheEntity()
    {
        var sandbox = CreateSandbox(out _);
        sandbox.SetValue("probe", "");

        await sandbox.ExecuteAsync("""
            import * as directory from 'directory';
            const first = directory.Find(u => u.Age > 18)[0];
            probe = Object.keys(first).join(',') + '|' + typeof first.PasswordHash;
            """);

        // The entity's PasswordHash never leaves the host — the projection did.
        Assert.Equal("Name,Department|undefined", sandbox.GetValue<string>("probe"));
    }

    [Fact]
    public async Task RuleCannotCaptureAnythingAmbient()
    {
        // With engine: null there is no closure resolution, so a free
        // identifier fails translation instead of silently resolving.
        var sandbox = CreateSandbox(out _);

        var ex = await Assert.ThrowsAsync<JsSandboxException>(() => sandbox.ExecuteAsync("""
            import * as directory from 'directory';
            directory.Find(u => u.Name === somethingAmbient);
            """));

        Assert.Contains("Unresolved identifier", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SandboxValue_CanParameterizeARule_AndArrivesAsAConstant()
    {
        // The realistic ABAC shape: the host supplies the subject's attributes
        // as values and the rule refers to them by name.
        var sandbox = CreateSandbox(out var module);
        sandbox.SetValue("allowedDepartment", "sales");
        sandbox.SetValue("minimumAge", 18);
        sandbox.SetValue("names", Array.Empty<string>());

        await sandbox.ExecuteAsync("""
            import * as directory from 'directory';
            names = directory
                .Find(u => u.Department === allowedDepartment && u.Age >= minimumAge)
                .map(m => m.Name);
            """);

        Assert.Equal(["Cara"], sandbox.GetValue<string[]>("names")!);

        // The value is baked in as a literal, not as a call on a host object —
        // which is exactly what makes this safe inside the sandbox.
        Assert.Equal(
            "u => ((u.Department == \"sales\") AndAlso (u.Age >= 18))",
            module.LastPredicate?.ToString());
    }

    [Fact]
    public async Task ModuleCallInsideARule_IsEvaluatedOnceAndFoldedToAConstant()
    {
        // The shape a real policy needs: ask the host something mid-rule. The
        // call does not depend on the row, so it is answered during translation
        // and the provider only ever sees the result.
        var sandbox = CreateSandbox(out var module);
        sandbox.SetValue("names", Array.Empty<string>());

        await sandbox.ExecuteAsync("""
            import * as directory from 'directory';
            names = directory
                .Find(u => u.Department === directory.Setting('tenantDepartment'))
                .map(m => m.Name);
            """);

        Assert.Equal(["Cara"], sandbox.GetValue<string[]>("names")!);
        Assert.Equal("u => (u.Department == \"sales\")", module.LastPredicate?.ToString());
    }

    [Fact]
    public async Task ModuleCallInARule_SurfacesItsOwnValidation()
    {
        // The module decides which keys exist; an unknown one fails loudly at
        // translation time rather than producing a silently wrong query.
        var sandbox = CreateSandbox(out _);

        var ex = await Assert.ThrowsAsync<JsSandboxException>(() => sandbox.ExecuteAsync("""
            import * as directory from 'directory';
            directory.Find(u => u.Department === directory.Setting('../../etc/passwd'));
            """));

        Assert.Contains("unknown setting", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SandboxGuaranteesStillHold_WhileRulesAreTranslated()
    {
        var sandbox = CreateSandbox(out _);

        await sandbox.ExecuteAsync("""
            import * as directory from 'directory';
            const forbidden = ['System', 'importNamespace', 'fetch', 'require', 'NewObject', 'console'];
            for (const name of forbidden) {
                if (typeof globalThis[name] !== 'undefined') throw new Error(name + ' is reachable');
            }
            const r = directory.Find(u => u.Age > 0)[0];
            if (typeof r.GetType !== 'undefined') throw new Error('CLR members leaked');
            """);
    }
}
