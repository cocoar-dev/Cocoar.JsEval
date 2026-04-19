using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Cocoar.JsEval.TsDefinition;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Linq;

public class LinqTsContributorTests
{
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
        Assert.Contains("guid(value: string): Guid", linq);
        Assert.Contains("decimal(value: string): Decimal", linq);
        Assert.Contains("long(value: string): Long", linq);
        Assert.Contains("today(): Date", linq);
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
