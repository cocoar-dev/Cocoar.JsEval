using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Cocoar.JsEval.TsDefinition;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Linq;

public class LinqTsContributorTests
{
    private static JsEngine BuildEngineWithLinq()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b.AddLinq());
        var sp = sc.BuildServiceProvider();
        return sp.CreateScope().ServiceProvider.GetRequiredService<JsEngine>();
    }

    // AddLinq() registers String + IEnumerable<T> extension methods with Jint so
    // the PascalCase C# aliases (Contains, StartsWith, Where, Any, …) that our
    // .d.ts declares actually WORK at plain runtime — not just in translator-
    // processed predicate lambdas.
    [Theory]
    [InlineData("'hello'.Contains('ell')",           true)]
    [InlineData("'hello'.StartsWith('he')",          true)]
    [InlineData("'hello'.EndsWith('lo')",            true)]
    [InlineData("'HELLO'.ToLower() === 'hello'",     true)]
    [InlineData("'hello'.ToUpper() === 'HELLO'",     true)]
    [InlineData("'  x  '.Trim() === 'x'",            true)]
    public void StringAliases_ResolveAtRuntime(string script, bool expected)
    {
        using var engine = BuildEngineWithLinq();
        var actual = engine.EvaluateExpression(script).ToObject() is true;
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("[1,2,3].Any(x => x > 2)",           true)]
    [InlineData("[1,2,3].All(x => x > 0)",           true)]
    [InlineData("[1,2,3].Contains(2)",               true)]
    [InlineData("[1,2,3].Contains(99)",              false)]
    public void ArrayAliases_ResolveAtRuntime_Predicate(string script, bool expected)
    {
        using var engine = BuildEngineWithLinq();
        var actual = engine.EvaluateExpression(script).ToObject() is true;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ArrayAliases_Where_FiltersAtRuntime()
    {
        using var engine = BuildEngineWithLinq();
        // JS numbers marshal back as System.Double regardless of the method's
        // C# return type — compare as double.
        var count = (double)engine.EvaluateExpression("[1,2,3,4,5].Where(x => x > 2).Count()").ToObject()!;
        Assert.Equal(3d, count);
    }

    [Fact]
    public void ArrayAliases_Select_MapsAtRuntime()
    {
        using var engine = BuildEngineWithLinq();
        var first = (double)engine.EvaluateExpression("[1,2,3].Select(x => x * 10).First()").ToObject()!;
        Assert.Equal(10d, first);
    }

    [Fact]
    public void LinqGlobal_GuidParses()
    {
        using var engine = BuildEngineWithLinq();
        var g = (Guid)engine.EvaluateExpression("linq.guid('00000000-0000-0000-0000-000000000001')").ToObject()!;
        Assert.Equal(new Guid("00000000-0000-0000-0000-000000000001"), g);
    }

    // AddLinq must register LinqTsContributor in DI so TsDefinitionService
    // automatically picks up linq.d.ts — no separate wiring needed for Monaco
    // consumers to see the `linq.*` global.
    [Fact]
    public void AddLinq_RegistersLinqDtsContributor()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b.AddLinq());
        sc.AddTsDefinition();
        using var sp = sc.BuildServiceProvider();

        var defs = sp.GetRequiredService<TsDefinitionService>().GetTsDefinitions();

        Assert.True(defs.ContainsKey("linq.d.ts"));
        var linq = defs["linq.d.ts"];
        Assert.Contains("declare const linq:", linq);
        // Return types must match what TsDefinition actually emits for the
        // underlying .NET types — System.Guid for Guid, number for numeric
        // primitives, Date for DateTime/DateOnly/TimeOnly.
        Assert.Contains("guid(value: string): System.Guid", linq);
        Assert.Contains("decimal(value: string): number", linq);
        Assert.Contains("long(value: string): number", linq);
        Assert.Contains("today(): Date", linq);

        // The second file the Linq package owns — augments String + Array<T>
        // with C# LINQ method aliases (mirrors the translator's method-map).
        Assert.True(defs.ContainsKey("cocoar-jseval-linq.d.ts"));
        var aliases = defs["cocoar-jseval-linq.d.ts"];
        Assert.Contains("interface String", aliases);
        Assert.Contains("Contains(value: string): boolean", aliases);
        Assert.Contains("interface Array<T>", aliases);
        Assert.Contains("Any(predicate", aliases);
    }

    [Fact]
    public void TsDefinitionsWithoutAddLinq_DoNotIncludeLinqDts()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        sc.AddTsDefinition();
        using var sp = sc.BuildServiceProvider();

        var defs = sp.GetRequiredService<TsDefinitionService>().GetTsDefinitions();

        Assert.False(defs.ContainsKey("linq.d.ts"));
    }
}
