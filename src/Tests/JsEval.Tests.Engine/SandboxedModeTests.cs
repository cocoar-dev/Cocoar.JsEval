using System;
using Cocoar.JsEval.Engine;
using Jint;
using Jint.Native;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// <c>Sandboxed()</c> is a latch, not a suggestion: once applied, nothing may
/// widen the engine again, and it refuses to be applied on top of a grant that
/// already happened. Without that the guarantee would silently depend on the
/// order the builder was written in.
/// </summary>
public class SandboxedModeTests
{
    private static JsEngine CreateEngine(Action<JsEvalBuilder> configure)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(configure);
        return sc.BuildServiceProvider().GetRequiredService<JsEngine>();
    }

    // ---------------------------------------------------------------------
    // The latch
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("fetch")]
    [InlineData("require")]
    [InlineData("newobject")]
    [InlineData("timers")]
    [InlineData("console")]
    [InlineData("assemblies")]
    public void WideningAfterSandboxed_Throws(string capability)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CreateEngine(b =>
        {
            b.Sandboxed();
            Apply(b, capability);
        }));

        Assert.Contains("sandboxed engine", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("require")]
    [InlineData("newobject")]
    [InlineData("timers")]
    [InlineData("console")]
    [InlineData("assemblies")]
    public void SandboxedAfterWidening_AlsoThrows(string capability)
    {
        // The other order must fail too — otherwise the guarantee would depend
        // on where in the chain Sandboxed() happens to sit.
        var ex = Assert.Throws<InvalidOperationException>(() => CreateEngine(b =>
        {
            Apply(b, capability);
            b.Sandboxed();
        }));

        Assert.Contains("cannot be applied after", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NarrowingAfterSandboxed_IsStillAllowed()
    {
        // AllowOnly and DenyTypes restrict rather than grant, so they stay open.
        using var engine = CreateEngine(b => b
            .Sandboxed()
            .AllowOnly(a => a.Member((Holder h) => h.Name))
            .DenyTypes(typeof(Secret)));
        engine.SetValue("host", new Holder());

        Assert.Equal("ok", engine.EvaluateExpression("String(host.Name)").AsString());
        Assert.Equal("undefined", engine.EvaluateExpression("typeof host.Hidden").AsString());
    }

    // ---------------------------------------------------------------------
    // What the mode actually locks down
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("eval('1+1')")]
    [InlineData("new Function('return 1')()")]
    [InlineData("({}).constructor.constructor('return globalThis')()")]
    public void StringCompilation_IsDisabled(string expression)
    {
        using var engine = CreateEngine(b => b.Sandboxed());

        Assert.ThrowsAny<Exception>(() => engine.EvaluateExpression(expression));
    }

    [Theory]
    [InlineData("Atomics")]
    [InlineData("SharedArrayBuffer")]
    [InlineData("fetch")]
    [InlineData("require")]
    [InlineData("NewObject")]
    [InlineData("console")]
    [InlineData("setTimeout")]
    public void HostAndSharedMemoryGlobals_AreAbsent(string name)
    {
        using var engine = CreateEngine(b => b.Sandboxed());

        Assert.Equal("undefined", engine.EvaluateExpression($"typeof {name}").AsString());
    }

    [Fact]
    public void BuiltInPrototypes_AreFrozen()
    {
        using var engine = CreateEngine(b => b.Sandboxed());

        Assert.ThrowsAny<Exception>(() => engine.Evaluate("Object.prototype.polluted = true;"));
        Assert.ThrowsAny<Exception>(() => engine.Evaluate("Array.prototype[1] = 'injected';"));
    }

    [Fact]
    public void GetTypeAndReflection_AreBlocked()
    {
        using var engine = CreateEngine(b => b.Sandboxed());
        engine.SetValue("host", new Holder());

        Assert.ThrowsAny<Exception>(() => engine.EvaluateExpression("host.GetType()"));
    }

    [Fact]
    public void RunawayScripts_AreStopped()
    {
        using var engine = CreateEngine(b => b.Sandboxed().WithMaxStatements(1_000));

        Assert.ThrowsAny<Exception>(() => engine.Evaluate("while (true) { }"));
    }

    [Fact]
    public void OrdinaryInteropStillWorks()
    {
        // The point of this surface: real CLR objects and real method calls.
        using var engine = CreateEngine(b => b.Sandboxed());
        engine.SetValue("host", new Holder());

        Assert.Equal("hi bob", engine.EvaluateExpression("String(host.Greet('bob'))").AsString());
    }

    [Fact]
    public void SandboxedAlone_DoesNotRestrictWhatAPassedObjectReaches()
    {
        // The name oversells it: Sandboxed() hardens the runtime, it does not
        // narrow the object graph. A passed object still grants everything
        // reachable from it until AllowOnly or DenyTypes says otherwise.
        using var engine = CreateEngine(b => b.Sandboxed());
        engine.SetValue("holder", new Reachable());

        Assert.Equal("hunter2", engine.EvaluateExpression(
            "String(holder.Inner.Secret)").AsString());
    }

    [Fact]
    public void SandboxedPlusAllowOnly_ClosesTheGraph()
    {
        using var engine = CreateEngine(b => b
            .Sandboxed()
            .AllowOnly(a => a.Member((Reachable r) => r.Label)));
        engine.SetValue("holder", new Reachable());

        Assert.Equal("label", engine.EvaluateExpression("String(holder.Label)").AsString());
        Assert.Equal("undefined", engine.EvaluateExpression("typeof holder.Inner").AsString());
    }

    public sealed class Inner { public string Secret => "hunter2"; }
    public sealed class Reachable
    {
        public string Label => "label";
        public Inner Inner { get; } = new();
    }

    [Fact]
    public void WithoutSandboxed_NothingChanges()
    {
        using var engine = CreateEngine(b => b.EnableConsole());

        Assert.Equal("object", engine.EvaluateExpression("typeof console").AsString());
    }

    private static void Apply(JsEvalBuilder b, string capability)
    {
        switch (capability)
        {
            case "fetch": b.EnableFetch(); break;
            case "require": b.EnableRequire(); break;
            case "newobject": b.EnableNewObject(); break;
            case "timers": b.EnableTimers(); break;
            case "console": b.EnableConsole(); break;
            case "assemblies": b.AllowAssemblies(typeof(Holder).Assembly); break;
        }
    }

    public sealed class Secret { public string Value => "s"; }

    public sealed class Holder
    {
        public string Name => "ok";
        public string Hidden => "no";
        public string Greet(string who) => $"hi {who}";
    }
}
