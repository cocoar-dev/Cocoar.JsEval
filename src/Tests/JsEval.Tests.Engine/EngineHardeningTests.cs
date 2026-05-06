using System;
using System.IO;
using System.Threading.Tasks;
using Cocoar.JsEval.Engine;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// Pins the 4.0 minimal-mode defaults: unsafe globals are off-by-default
/// and only available after explicit opt-in via the corresponding builder
/// flag. Mirrors the test matrix from
/// <c>.local/security-untrusted-script-hardening.md</c> (F1–F5 + Part D).
/// </summary>
public class EngineHardeningTests
{
    private static JsEngine CreateEngine(Action<JsEvalBuilder>? configure = null)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(configure ?? (_ => { }));
        return sc.BuildServiceProvider().GetRequiredService<JsEngine>();
    }

    private static string TypeOf(JsEngine engine, string name) =>
        engine.EvaluateExpression($"typeof {name}").AsString();

    // ---------------------------------------------------------------------
    // F1 — NewObject
    // ---------------------------------------------------------------------

    [Fact]
    public void NewObject_DefaultUnavailable()
    {
        using var engine = CreateEngine();
        Assert.Equal("undefined", TypeOf(engine, "NewObject"));
    }

    [Fact]
    public void NewObject_Enabled_AliasResolves()
    {
        using var engine = CreateEngine(b => b
            .EnableNewObject()
            .AddTypeAlias<HardeningSample>("HardeningSample"));

        var result = engine.EvaluateExpression("NewObject('HardeningSample')");

        Assert.IsType<HardeningSample>(result.ToObject());
    }

    [Fact]
    public void NewObject_Enabled_WithoutFallback_UnknownTypeReturnsNull()
    {
        // Without EnableNewObjectAssemblyFallback, NewObject is alias-only —
        // arbitrary CLR types must NOT resolve, even when the assembly is
        // loaded into AppDomain.
        using var engine = CreateEngine(b => b.EnableNewObject());

        var result = engine.EvaluateExpression(
            "NewObject('System.IO.FileInfo', ['C:\\\\Windows\\\\System32\\\\drivers\\\\etc\\\\hosts'])");

        Assert.True(result.IsNull(), $"Expected null, got {result.ToObject()?.GetType().FullName ?? "null"}");
    }

    [Fact]
    public void NewObject_AssemblyFallback_OnlyListedAssembliesResolve()
    {
        using var engine = CreateEngine(b => b
            .EnableNewObject()
            .EnableNewObjectAssemblyFallback(typeof(HardeningSample).Assembly));

        // Type from the listed (test) assembly resolves via fallback.
        var listed = engine.EvaluateExpression(
            $"NewObject('{typeof(HardeningSample).FullName}')");
        Assert.IsType<HardeningSample>(listed.ToObject());

        // Type from a non-listed assembly (System.IO) does not.
        var notListed = engine.EvaluateExpression(
            "NewObject('System.IO.FileInfo', ['C:\\\\some\\\\path'])");
        Assert.True(notListed.IsNull());
    }

    // ---------------------------------------------------------------------
    // F2 — require
    // ---------------------------------------------------------------------

    [Fact]
    public void Require_DefaultUnavailable()
    {
        using var engine = CreateEngine();
        Assert.Equal("undefined", TypeOf(engine, "require"));
    }

    [Fact]
    public void Require_Enabled_IsAvailable()
    {
        using var engine = CreateEngine(b => b.EnableRequire());
        Assert.Equal("function", TypeOf(engine, "require"));
    }

    // ---------------------------------------------------------------------
    // F3 — exit (permanently removed in 4.0)
    // ---------------------------------------------------------------------

    [Fact]
    public void Exit_PermanentlyRemoved_NoFlagAvailable()
    {
        // No EnableExit() exists. Even with every other flag on, exit() stays
        // gone — it left the engine permanently broken. Use IIFE early-return.
        using var engine = CreateEngine(b => b
            .EnableNewObject()
            .EnableRequire()
            .EnableTimers()
            .EnableConsole()
            .EnableFetch());
        Assert.Equal("undefined", TypeOf(engine, "exit"));
    }

    // ---------------------------------------------------------------------
    // F4 — timers
    // ---------------------------------------------------------------------

    [Fact]
    public void Timers_DefaultUnavailable()
    {
        using var engine = CreateEngine();
        Assert.Equal("undefined", TypeOf(engine, "setTimeout"));
        Assert.Equal("undefined", TypeOf(engine, "setInterval"));
        Assert.Equal("undefined", TypeOf(engine, "clearTimeout"));
        Assert.Equal("undefined", TypeOf(engine, "clearInterval"));
    }

    [Fact]
    public void Timers_Enabled_AreAvailable()
    {
        using var engine = CreateEngine(b => b.EnableTimers());
        Assert.Equal("function", TypeOf(engine, "setTimeout"));
        Assert.Equal("function", TypeOf(engine, "setInterval"));
        Assert.Equal("function", TypeOf(engine, "clearTimeout"));
        Assert.Equal("function", TypeOf(engine, "clearInterval"));
    }

    // ---------------------------------------------------------------------
    // F5 — console
    // ---------------------------------------------------------------------

    [Fact]
    public void Console_DefaultUnavailable()
    {
        using var engine = CreateEngine();
        Assert.Equal("undefined", TypeOf(engine, "console"));
    }

    [Fact]
    public void Console_Enabled_IsAvailable()
    {
        using var engine = CreateEngine(b => b.EnableConsole());
        Assert.Equal("object", TypeOf(engine, "console"));
        Assert.Equal("function", TypeOf(engine, "console.log"));
    }

    // ---------------------------------------------------------------------
    // Always-on safe primitives — must remain available without opt-in
    // ---------------------------------------------------------------------

    [Fact]
    public void SafePrimitives_AlwaysAvailableByDefault()
    {
        using var engine = CreateEngine();

        Assert.Equal("function", TypeOf(engine, "btoa"));
        Assert.Equal("function", TypeOf(engine, "atob"));
        Assert.Equal("object",   TypeOf(engine, "performance"));
        Assert.Equal("function", TypeOf(engine, "TextEncoder"));
        Assert.Equal("function", TypeOf(engine, "TextDecoder"));
        Assert.Equal("function", TypeOf(engine, "structuredClone"));
    }

    // ---------------------------------------------------------------------
    // Part D — Engine constraints
    // ---------------------------------------------------------------------

    [Fact]
    public void EngineConstraints_TimeoutTerminatesRunawayLoop()
    {
        // Override the 10s default with a tight 100ms cap so the test stays fast.
        // Proves that WithExecutionTimeout actually rewires Jint's TimeoutInterval
        // and that the timeout surfaces (not silently swallowed).
        using var engine = CreateEngine(b => b
            .WithExecutionTimeout(TimeSpan.FromMilliseconds(100)));

        Assert.Throws<TimeoutException>(() =>
            engine.Evaluate("while (true) { }"));
    }

    [Fact]
    public void EngineConstraints_MaxStatementsTriggersOnTightLoop()
    {
        using var engine = CreateEngine(b => b.WithMaxStatements(1_000));

        // ~10k iterations × 2 statements should easily exceed the 1k cap.
        Assert.Throws<StatementsCountOverflowException>(() =>
            engine.Evaluate("var i = 0; while (i < 10000) { i++; }"));
    }

    [Fact]
    public async Task EngineConstraints_TimeoutSurfacesViaExecuteAsync()
    {
        // ExecuteAsync's catch-when filter only swallows ExecutionCanceledException
        // when our own CTS triggered it (= Stop()). Timeouts raise TimeoutException
        // (a distinct exception type in Jint 4.8) and propagate naturally.
        using var engine = CreateEngine(b => b
            .WithExecutionTimeout(TimeSpan.FromMilliseconds(100)));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await engine.ExecuteAsync("while (true) { }"));
    }

    [Fact]
    public async Task EngineConstraints_StopRemainsSilentInExecuteAsync()
    {
        // Programmatic Stop() cancels via our CancellationTokenSource. Jint then
        // throws ExecutionCanceledException, which ExecuteAsync's filtered catch
        // swallows (matches the documented contract preserved across 4.0).
        using var engine = CreateEngine();
        engine.Stop();
        await engine.ExecuteAsync("export const x = 1;"); // no exception
    }
}

/// <summary>POCO used to verify the NewObject alias and assembly-fallback paths.</summary>
public sealed class HardeningSample
{
    public string Tag { get; set; } = "default";
}
