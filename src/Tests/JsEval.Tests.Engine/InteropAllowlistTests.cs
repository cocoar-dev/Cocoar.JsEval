using System;
using System.Collections.Generic;
using Cocoar.JsEval.Engine;
using Jint;
using Jint.Native;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// Pins the interop allow/deny surface. Passing an object to a script normally
/// grants everything reachable from it; these options are what turn that into a
/// surface the host can enumerate.
/// </summary>
public class InteropAllowlistTests
{
    public sealed class FakeDbContext
    {
        public string ConnectionString => "Host=prod;Password=hunter2";
        public string Drop() => "dropped";
    }

    public sealed class Tenant
    {
        public string Id => "acme";
        public FakeDbContext Database { get; } = new();
    }

    public sealed class Order
    {
        public decimal Total => 99m;
        public Tenant Tenant { get; } = new();
    }

    public sealed class Customer
    {
        public string Name => "Alice";
        public string PasswordHash => "top-secret";
        public DateTime CreatedAt => new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public List<Order> Orders { get; } = [new Order()];
        public string Greet(string who) => $"hi {who}";
        public string Nuke() => "boom";

        // Loosely typed on purpose: the declared type says nothing about what
        // actually comes back, so only a runtime check can catch it.
        public object Loose => new FakeDbContext();
    }

    private static JsEngine CreateEngine(Action<JsEvalBuilder> configure)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(configure);
        return sc.BuildServiceProvider().GetRequiredService<JsEngine>();
    }

    private static JsEngine Allowlisted()
    {
        var engine = CreateEngine(b => b.AllowOnly(a => a
            .Member((Customer c) => c.Name)
            .Member((Customer c) => c.CreatedAt)
            .Member((Customer c) => c.Orders)
            .Method((Customer c) => c.Greet(default!))
            .Member((Order o) => o.Total)
            .Type<DateTime>()));
        engine.SetValue("customer", new Customer());
        return engine;
    }

    // ---------------------------------------------------------------------
    // Allowed
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("String(customer.Name)", "Alice")]
    [InlineData("String(customer.Greet('bob'))", "hi bob")]
    [InlineData("String(customer.Orders[0].Total)", "99")]
    [InlineData("String(customer.Orders.length)", "1")]
    public void DeclaredMembers_AreReachable(string expression, string expected)
    {
        using var engine = Allowlisted();

        Assert.Equal(expected, engine.EvaluateExpression(expression).AsString());
    }

    [Fact]
    public void DateTimeProperty_CrossesAsAJsDate_AndIsUnaffectedByTheFilter()
    {
        // A DateTime is converted to a JS Date rather than wrapped, so its
        // members are JS members and never reach the member filter at all.
        using var engine = Allowlisted();

        Assert.Equal(2024d, engine.EvaluateExpression("customer.CreatedAt.getFullYear()").AsNumber());
    }

    [Fact]
    public void StringMembersOfAnAllowedProperty_StillWork()
    {
        // A returned string is a JS primitive, not a wrapped CLR object, so the
        // member filter never sees startsWith and must not break it.
        using var engine = Allowlisted();

        Assert.True(engine.EvaluateExpression("customer.Name.startsWith('A')").AsBoolean());
    }

    // ---------------------------------------------------------------------
    // Not allowed
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("customer.PasswordHash")]
    [InlineData("customer.Orders[0].Tenant")]
    [InlineData("customer.Typo")]
    public void UndeclaredMembers_DoNotExistForTheScript(string expression)
    {
        using var engine = Allowlisted();

        Assert.Equal("undefined", engine.EvaluateExpression($"typeof {expression}").AsString());
    }

    [Fact]
    public void UndeclaredMethod_IsNotCallable()
    {
        using var engine = Allowlisted();

        Assert.ThrowsAny<Exception>(() => engine.EvaluateExpression("customer.Nuke()"));
    }

    [Fact]
    public void LoudDenials_AreOptInAndCostJsonStringify()
    {
        // Documents the trade-off rather than choosing for the host: the same
        // Jint switch that reports a denial also fires on JSON.stringify's
        // toJSON probe, so enabling it breaks serializing wrapped CLR objects.
        var engine = CreateEngine(b => b
            .AllowOnly(a => a.Member((Customer c) => c.Name))
            .ConfigureJint(o => o.Interop.ThrowOnUnresolvedMember = true));
        engine.SetValue("customer", new Customer());

        Assert.Equal("Alice", engine.EvaluateExpression("String(customer.Name)").AsString());
        Assert.ThrowsAny<Exception>(() => engine.EvaluateExpression("customer.PasswordHash"));
        Assert.ThrowsAny<Exception>(() => engine.EvaluateExpression("JSON.stringify(customer)"));
    }

    [Fact]
    public void TransitiveGraph_StopsAtTheFirstUndeclaredHop()
    {
        using var engine = Allowlisted();

        Assert.ThrowsAny<Exception>(() =>
            engine.EvaluateExpression("customer.Orders[0].Tenant.Database.Drop()"));
    }

    [Theory]
    [InlineData("Object.keys(customer).join(',')")]
    [InlineData("JSON.stringify(customer)")]
    public void EnumerationOnlyReportsDeclaredMembers(string expression)
    {
        using var engine = Allowlisted();

        var rendered = engine.EvaluateExpression(expression).AsString();

        Assert.DoesNotContain("PasswordHash", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Tenant", rendered, StringComparison.Ordinal);
        Assert.Contains("Name", rendered, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Denied types
    // ---------------------------------------------------------------------

    [Fact]
    public void DeniedType_IsUnreachableThroughADeclaredMember()
    {
        var engine = CreateEngine(b => b.DenyTypes(typeof(FakeDbContext)));
        engine.SetValue("tenant", new Tenant());

        Assert.Equal("acme", engine.EvaluateExpression("String(tenant.Id)").AsString());
        Assert.ThrowsAny<Exception>(() => engine.EvaluateExpression("String(tenant.Database.ConnectionString)"));
    }

    [Fact]
    public void DeniedType_IsAlsoCaughtWhenTheDeclaredTypeHidesIt()
    {
        // The declared type is object, so only the runtime check can refuse it.
        // Without that second layer a deny list would silently do nothing here.
        var engine = CreateEngine(b => b.DenyTypes(typeof(FakeDbContext)));
        engine.SetValue("customer", new Customer());

        var ex = Assert.ThrowsAny<Exception>(() =>
            engine.EvaluateExpression("String(customer.Loose.ConnectionString)"));

        Assert.Contains("denied", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DenyingABaseType_CoversDerivedTypes()
    {
        var engine = CreateEngine(b => b.DenyTypes(typeof(ContextBase)));
        engine.SetValue("holder", new DerivedHolder());

        Assert.ThrowsAny<Exception>(() => engine.EvaluateExpression("String(holder.Context.Secret)"));
    }

    public abstract class ContextBase { public string Secret => "s"; }
    public sealed class DerivedContext : ContextBase { }
    public sealed class DerivedHolder { public DerivedContext Context { get; } = new(); }

    // ---------------------------------------------------------------------
    // Defaults and the raw escape hatch
    // ---------------------------------------------------------------------

    [Fact]
    public void WithoutAllowOnly_InteropIsUnchanged()
    {
        // The restriction is opt-in; existing consumers keep full reach.
        var engine = CreateEngine(_ => { });
        engine.SetValue("customer", new Customer());

        Assert.Equal("top-secret", engine.EvaluateExpression("String(customer.PasswordHash)").AsString());
    }

    [Fact]
    public void ConfigureJint_ReachesOptionsThatOnlyApplyAtConstruction()
    {
        // RegisterEngineConfigurator runs after the engine exists and cannot set
        // these; ConfigureJint is the hook that can.
        var engine = CreateEngine(b => b.ConfigureJint(o =>
        {
            o.Interop.AllowGetType = false;
            o.Interop.ThrowOnUnresolvedMember = true;
        }));
        engine.SetValue("customer", new Customer());

        Assert.ThrowsAny<Exception>(() => engine.EvaluateExpression("customer.GetType()"));
    }
}
