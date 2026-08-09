using System;
using Cocoar.JsEval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// A script can nest a structure deeply enough that serializing it recurses
/// past the .NET stack. That used to terminate the host process with an
/// uncatchable <c>StackOverflowException</c> on two ordinary paths — reading a
/// value back with <see cref="JsEngine.GetValue{T}"/>, and
/// <see cref="JsEngine.JsonStringify"/> — so these tests passing at all is the
/// regression check.
/// </summary>
public class JsonDepthGuardTests
{
    public sealed class Deep
    {
        public Deep? N { get; set; }
    }

    private static JsEngine CreateEngine(Action<JsEvalBuilder>? configure = null)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(configure ?? (_ => { }));
        return sc.BuildServiceProvider().GetRequiredService<JsEngine>();
    }

    private static JsEngine WithNesting(int depth, Action<JsEvalBuilder>? configure = null)
    {
        var engine = CreateEngine(configure);
        engine.Evaluate($"var data = {{}}; var r = data; for (var i = 0; i < {depth}; i++) {{ r.n = {{}}; r = r.n; }}");
        return engine;
    }

    [Fact]
    public void GetValue_OnAnOverlyNestedValue_ThrowsInsteadOfKillingTheProcess()
    {
        using var engine = WithNesting(4000);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.GetValue<Deep>("data"));

        Assert.Contains("nests deeper", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonStringify_OnAnOverlyNestedValue_ThrowsInsteadOfKillingTheProcess()
    {
        using var engine = WithNesting(4000);

        Assert.Throws<InvalidOperationException>(
            () => engine.JsonStringify(engine.EvaluateExpression("data")));
    }

    [Fact]
    public void OrdinaryNesting_StillRoundTrips()
    {
        using var engine = WithNesting(20);

        var json = engine.JsonStringify(engine.EvaluateExpression("data"));

        Assert.Contains("\"n\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLimitIsConfigurable()
    {
        using var engine = WithNesting(100, b => b.ConfigureJint(_ => { }));
        engine.Options.WithMaxJsonDepth(10);

        Assert.Throws<InvalidOperationException>(
            () => engine.JsonStringify(engine.EvaluateExpression("data")));
    }

    [Fact]
    public void CyclesAreStillReportedAsCycles_NotAsDepth()
    {
        // The guard tracks the current path, so a back-reference is recognised
        // as a cycle and left to the serializer, which names it precisely.
        using var engine = CreateEngine();
        engine.Evaluate("var data = { a: 1 }; data.self = data;");

        var ex = Assert.ThrowsAny<Exception>(
            () => engine.JsonStringify(engine.EvaluateExpression("data")));

        Assert.DoesNotContain("nests deeper", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WideButShallowStructures_AreUnaffected()
    {
        using var engine = CreateEngine();
        engine.Evaluate("var data = []; for (var i = 0; i < 5000; i++) { data.push({ i: i }); }");

        var json = engine.JsonStringify(engine.EvaluateExpression("data"));

        Assert.Contains("\"i\":4999", json, StringComparison.Ordinal);
    }
}
