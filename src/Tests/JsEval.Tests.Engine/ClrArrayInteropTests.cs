using System;
using System.Collections.Generic;
using Cocoar.JsEval.Engine;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// Pins how CLR collections behave once they cross into script. The contract
/// depends on <c>Options.Interop.ArrayConversion</c>, whose default Jint
/// changed in 4.14, so these tests exist to make any future shift visible here
/// rather than in a consumer's scripts.
///
/// A CLR <c>T[]</c> is exposed as a live view: element writes and in-place
/// reordering reach the underlying array, while the two operations a
/// fixed-size array cannot honour — <c>push</c> and assigning <c>length</c> —
/// throw. A resizable <see cref="List{T}"/> has neither restriction.
/// </summary>
public class ClrArrayInteropTests
{
    private static JsEngine CreateEngine(Action<JsEvalBuilder>? configure = null)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(configure ?? (_ => { }));
        return sc.BuildServiceProvider().GetRequiredService<JsEngine>();
    }

    private static JsEngine WithHost(out ArrayHolder host)
    {
        var engine = CreateEngine();
        host = new ArrayHolder();
        engine.SetValue("host", host);
        return engine;
    }

    // ---------------------------------------------------------------------
    // Identification
    // ---------------------------------------------------------------------

    [Fact]
    public void ClrArray_IsNotAJsArray()
    {
        // A live view over a CLR array is array-like, not a real JS array.
        // Scripts branching on Array.isArray() must treat this as the answer.
        using var engine = WithHost(out _);

        Assert.False(engine.EvaluateExpression("Array.isArray(host.Tags)").AsBoolean());
    }

    [Fact]
    public void ClrList_IsNotAJsArray()
    {
        using var engine = CreateEngine();
        engine.SetValue("tags", new List<string> { "a", "b", "c" });

        Assert.False(engine.EvaluateExpression("Array.isArray(tags)").AsBoolean());
    }

    // ---------------------------------------------------------------------
    // Reading
    // ---------------------------------------------------------------------

    [Fact]
    public void ClrArray_SupportsLengthAndIndexedReads()
    {
        using var engine = WithHost(out _);

        Assert.Equal(3d, engine.EvaluateExpression("host.Tags.length").AsNumber());
        Assert.Equal("b", engine.EvaluateExpression("host.Tags[1]").AsString());
        Assert.Equal("undefined", engine.EvaluateExpression("typeof host.Tags[99]").AsString());
    }

    [Theory]
    [InlineData("host.Tags.map(x => x + '!').join(',')", "a!,b!,c!")]
    [InlineData("host.Tags.filter(x => x !== 'b').join(',')", "a,c")]
    [InlineData("host.Tags.slice(1).join(',')", "b,c")]
    [InlineData("host.Tags.join('-')", "a-b-c")]
    [InlineData("[...host.Tags].join(',')", "a,b,c")]
    [InlineData("Object.keys(host.Tags).join(',')", "0,1,2")]
    [InlineData("JSON.stringify(host.Tags)", "[\"a\",\"b\",\"c\"]")]
    public void ClrArray_SupportsNonMutatingArrayMethods(string expression, string expected)
    {
        using var engine = WithHost(out _);

        Assert.Equal(expected, engine.EvaluateExpression(expression).AsString());
    }

    [Fact]
    public void ClrArray_IsIterableWithForOf()
    {
        using var engine = WithHost(out _);
        engine.Evaluate("var joined = ''; for (const t of host.Tags) { joined += t; }");

        Assert.Equal("abc", engine.GetValue<string>("joined"));
    }

    // ---------------------------------------------------------------------
    // Writes reach the CLR array
    // ---------------------------------------------------------------------

    [Fact]
    public void IndexedWrite_ReachesTheClrArray()
    {
        using var engine = WithHost(out var host);
        engine.Evaluate("host.Tags[0] = 'z';");

        Assert.Equal(["z", "b", "c"], host.Tags);
    }

    [Fact]
    public void IndexedWriteThroughALocalAlias_ReachesTheClrArray()
    {
        using var engine = WithHost(out var host);
        engine.Evaluate("const t = host.Tags; t[0] = 'z';");

        Assert.Equal(["z", "b", "c"], host.Tags);
    }

    [Fact]
    public void InPlaceReordering_ReachesTheClrArray()
    {
        using var engine = WithHost(out var host);
        engine.Evaluate("host.Tags.reverse();");

        Assert.Equal(["c", "b", "a"], host.Tags);
    }

    [Fact]
    public void Sort_ReachesTheClrArray()
    {
        using var engine = WithHost(out var host);
        engine.Evaluate("host.Tags.sort((a, b) => b.localeCompare(a));");

        Assert.Equal(["c", "b", "a"], host.Tags);
    }

    // ---------------------------------------------------------------------
    // Resizing is rejected rather than silently dropped
    // ---------------------------------------------------------------------

    [Fact]
    public void Push_ThrowsAndLeavesTheClrArrayUntouched()
    {
        using var engine = WithHost(out var host);

        var ex = Assert.Throws<JavaScriptException>(() => engine.Evaluate("host.Tags.push('d');"));

        Assert.Contains("fixed-size", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["a", "b", "c"], host.Tags);
    }

    [Fact]
    public void LengthAssignment_ThrowsAndLeavesTheClrArrayUntouched()
    {
        using var engine = WithHost(out var host);

        Assert.Throws<JavaScriptException>(() => engine.Evaluate("host.Tags.length = 1;"));
        Assert.Equal(["a", "b", "c"], host.Tags);
    }

    // ---------------------------------------------------------------------
    // A resizable List<T> keeps full mutability
    // ---------------------------------------------------------------------

    [Fact]
    public void ClrList_SupportsPushAndWritesThrough()
    {
        using var engine = WithHost(out var host);
        engine.Evaluate("host.TagList.push('d'); host.TagList[0] = 'z';");

        Assert.Equal(["z", "b", "c", "d"], host.TagList);
    }

    // ---------------------------------------------------------------------
    // Wrapper identity
    // ---------------------------------------------------------------------

    [Fact]
    public void RepeatedPropertyReads_YieldTheSameArrayWrapper()
    {
        // A live view is not re-created per crossing, so identity holds and
        // script-attached state survives.
        using var engine = WithHost(out _);

        Assert.True(engine.EvaluateExpression("host.Tags === host.Tags").AsBoolean());
    }

    [Fact]
    public void WrappedObjectIdentity_AndScriptAttachedStateHold()
    {
        using var engine = WithHost(out _);

        Assert.True(engine.EvaluateExpression("host === host").AsBoolean());
        engine.Evaluate("host.marker = 42;");
        Assert.Equal(42d, engine.EvaluateExpression("host.marker").AsNumber());
    }

    // ---------------------------------------------------------------------
    // Inbound: script values into params arrays
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("host.Join('x', 'y')")]
    [InlineData("host.Join(...['x', 'y'])")]
    public void ScriptValues_MarshalIntoParamsArrays(string expression)
    {
        // Inbound arrays are unaffected by the live-view conversion — a script
        // still passes an ordinary JS array to a params parameter.
        using var engine = WithHost(out _);

        Assert.Equal("x|y", engine.EvaluateExpression(expression).AsString());
    }

    public sealed class ArrayHolder
    {
        public string[] Tags { get; set; } = ["a", "b", "c"];
        public List<string> TagList { get; set; } = ["a", "b", "c"];
        public string[] GetTags() => Tags;
        public string Join(params string[] parts) => string.Join("|", parts);
    }
}
