using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// Modules are the sandbox's capability channel: the host decides what a rule
/// can do by deciding what it registers. These tests pin that the channel works
/// end to end and that opening it does not reopen CLR interop — the module
/// instance stays on the host side and only JSON crosses.
/// </summary>
public class JsSandboxModuleTests
{
    // Stands in for a real module: it holds a dependency the script must never
    // reach, and exposes deliberately narrow operations.
    public sealed class CustomerModule : IJsModule
    {
        private readonly CustomerRepository _repository = new();

        public int Calls { get; private set; }

        public bool ExistsByEmail(string email)
        {
            Calls++;
            return _repository.ExistsByEmail(email);
        }

        public CustomerDto Lookup(string email) => new() { Email = email, Score = 42, Tags = ["vip"] };

        public async Task<bool> ExistsAsync(string email)
        {
            await Task.Delay(1);
            return _repository.ExistsByEmail(email);
        }

        public int Add(int a, int b = 10) => a + b;

        public void Boom() => throw new InvalidOperationException("module blew up");

        // Deliberately hostile: hands back its own internal dependency.
        public CustomerRepository Leak() => _repository;
    }

    public sealed class CustomerRepository
    {
        public string ConnectionString => "Server=prod;Password=hunter2";
        public bool ExistsByEmail(string email) => email == "taken@example.com";
        public void DropEverything() => throw new InvalidOperationException("must never be callable");
    }

    public sealed class CustomerDto
    {
        public string Email { get; set; } = "";
        public int Score { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    private static JsSandbox WithModule(out CustomerModule module)
    {
        var sandbox = new JsSandbox();
        module = new CustomerModule();
        sandbox.AddModule("customers", module);
        return sandbox;
    }

    // ---------------------------------------------------------------------
    // The channel works
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Script_CanImportAndCallAModule()
    {
        var sandbox = WithModule(out var module);
        sandbox.SetValue("result", false);

        await sandbox.ExecuteAsync("""
            import * as customers from 'customers';
            result = customers.ExistsByEmail('taken@example.com');
            """);

        Assert.True(sandbox.GetValue<bool>("result"));
        Assert.Equal(1, module.Calls);
    }

    [Fact]
    public async Task NamedImport_Works()
    {
        var sandbox = WithModule(out _);
        sandbox.SetValue("result", false);

        await sandbox.ExecuteAsync("""
            import { ExistsByEmail } from 'customers';
            result = ExistsByEmail('taken@example.com');
            """);

        Assert.True(sandbox.GetValue<bool>("result"));
    }

    [Fact]
    public async Task ModuleCall_CombinesWithSandboxValues()
    {
        var sandbox = WithModule(out _);
        sandbox.SetValue("customer", new CustomerDto { Email = "taken@example.com" });
        sandbox.SetValue("isDuplicate", false);

        await sandbox.ExecuteAsync("""
            import * as customers from 'customers';
            isDuplicate = customers.ExistsByEmail(customer.Email);
            """);

        Assert.True(sandbox.GetValue<bool>("isDuplicate"));
    }

    [Fact]
    public async Task AsyncModuleMethod_CanBeAwaited()
    {
        var sandbox = WithModule(out _);
        sandbox.SetValue("result", false);

        await sandbox.ExecuteAsync("""
            import * as customers from 'customers';
            result = await customers.ExistsAsync('taken@example.com');
            """);

        Assert.True(sandbox.GetValue<bool>("result"));
    }

    [Fact]
    public async Task OptionalParameter_UsesItsDefault()
    {
        var sandbox = WithModule(out _);
        sandbox.SetValue("sum", 0);

        await sandbox.ExecuteAsync("""
            import * as customers from 'customers';
            sum = customers.Add(5);
            """);

        Assert.Equal(15, sandbox.GetValue<int>("sum"));
    }

    // ---------------------------------------------------------------------
    // Opening the channel does not reopen CLR interop
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ReturnedObject_IsPlainJsonWithNoClrMembers()
    {
        var sandbox = WithModule(out _);
        sandbox.SetValue("probe", "");

        await sandbox.ExecuteAsync("""
            import * as customers from 'customers';
            const dto = customers.Lookup('a@b.c');
            if (typeof dto.GetType !== 'undefined' || typeof dto.getType !== 'undefined') {
                throw new Error('CLR members leaked through a module return value');
            }
            probe = dto.Email + '|' + dto.Score + '|' + dto.Tags.join(',');
            """);

        Assert.Equal("a@b.c|42|vip", sandbox.GetValue<string>("probe"));
    }

    [Fact]
    public async Task ObjectReturnedFromAModule_CannotBeCalledIntoTheHost()
    {
        // A module that hands back its own dependency is a host mistake, but
        // the JSON boundary still prevents the script from invoking anything on
        // it — only data crosses, never a callable CLR object.
        var sandbox = WithModule(out _);
        sandbox.SetValue("probe", "");

        await sandbox.ExecuteAsync("""
            import * as customers from 'customers';
            const leaked = customers.Leak();
            probe = typeof leaked.DropEverything + '|' + typeof leaked.ExistsByEmail;
            """);

        Assert.Equal("undefined|undefined", sandbox.GetValue<string>("probe"));
    }

    [Fact]
    public async Task ModulesDoNotBringBackHostGlobalsOrEval()
    {
        var sandbox = WithModule(out _);

        await sandbox.ExecuteAsync("""
            import * as customers from 'customers';
            const forbidden = ['System', 'importNamespace', 'fetch', 'require', 'NewObject', 'console'];
            for (const name of forbidden) {
                if (typeof globalThis[name] !== 'undefined') throw new Error(name + ' is reachable');
            }
            """);

        await Assert.ThrowsAsync<JsSandboxException>(() => sandbox.ExecuteAsync("eval('1+1');"));
    }

    [Fact]
    public async Task UnregisteredModule_CannotBeImported()
    {
        var sandbox = WithModule(out _);

        await Assert.ThrowsAsync<JsSandboxException>(() => sandbox.ExecuteAsync("""
            import * as fs from 'filesystem';
            """));
    }

    [Fact]
    public void ExecuteWithoutModuleSupport_CannotImport()
    {
        // The synchronous path has no module system at all.
        var sandbox = WithModule(out _);

        Assert.Throws<JsSandboxException>(() =>
            sandbox.Execute("import * as customers from 'customers';"));
    }

    // ---------------------------------------------------------------------
    // Failures stay inside the sandbox contract
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ModuleThrowing_SurfacesAsSandboxExceptionAndKeepsValues()
    {
        var sandbox = WithModule(out _);
        sandbox.SetValue("customer", new CustomerDto { Email = "keep@me" });

        var ex = await Assert.ThrowsAsync<JsSandboxException>(() => sandbox.ExecuteAsync("""
            import * as customers from 'customers';
            customer.Email = 'changed';
            customers.Boom();
            """));

        Assert.Contains("module blew up", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("keep@me", sandbox.GetValue<CustomerDto>("customer")?.Email);
    }

    [Fact]
    public async Task ResourceLimitsStillApplyToAModuleScript()
    {
        var sandbox = new JsSandbox(new JsSandboxOptions
        {
            ExecutionTimeout = TimeSpan.FromMilliseconds(100),
            MaxStatements = 1_000
        });
        sandbox.AddModule("customers", new CustomerModule());

        await Assert.ThrowsAsync<JsSandboxException>(() => sandbox.ExecuteAsync("""
            import * as customers from 'customers';
            while (true) { }
            """));
    }

    [Fact]
    public void DuplicateModuleName_IsRejected()
    {
        var sandbox = WithModule(out _);

        Assert.Throws<ArgumentException>(() => sandbox.AddModule("customers", new CustomerModule()));
    }
}
