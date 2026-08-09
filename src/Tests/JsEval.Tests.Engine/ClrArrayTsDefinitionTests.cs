using System.Collections.Generic;
using System.Linq;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.TsDefinition;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// The generated declarations must describe what the engine actually does with
/// a .NET array. Script receives a fixed-size live view, so a declaration of
/// plain <c>T[]</c> would offer <c>push</c> and <c>length</c> assignment in the
/// editor and then throw at runtime. Outbound arrays are therefore declared as
/// <c>ClrArray&lt;T&gt;</c>; inbound parameters stay <c>T[]</c>.
/// </summary>
public class ClrArrayTsDefinitionTests
{
    private static string RenderTags()
    {
        var rendered = new DefinitionBuilder().AddType<TagHolder>().Render();
        return string.Join("\n", rendered.Values);
    }

    [Fact]
    public void ArrayProperty_IsDeclaredAsClrArray()
    {
        var dts = RenderTags();

        Assert.Contains("Tags: ClrArray<string>", dts);
        Assert.DoesNotContain("Tags: string[]", dts);
    }

    [Fact]
    public void ArrayReturningMethod_IsDeclaredAsClrArray()
    {
        var dts = RenderTags();

        Assert.Contains("GetTags(): ClrArray<string>", dts);
    }

    [Fact]
    public void ParamsArrayParameter_StaysAPlainArray()
    {
        // Inbound arrays are ordinary JS arrays — declaring them ClrArray would
        // wrongly reject a script passing an array literal it built itself.
        var dts = RenderTags();

        Assert.Contains("parts: string[]", dts);
        Assert.DoesNotContain("parts: ClrArray<string>", dts);
    }

    [Fact]
    public void ResizableList_StaysAMutableArray()
    {
        var dts = RenderTags();

        Assert.Contains("TagList: Array<string>", dts);
    }

    [Fact]
    public void GlobalDefinitions_DeclareTheClrArrayHelper()
    {
        // Without the helper in global.d.ts every generated ClrArray<T>
        // reference would be an unresolved type in the editor.
        var sc = new ServiceCollection();
        sc.AddJsEval(_ => { });
        using var sp = sc.BuildServiceProvider();
        var service = new TsDefinitionService(sp.GetRequiredService<IJsModuleRegistry>());

        var global = service.GetTsDefinitions()["global.d.ts"];

        Assert.Contains("interface ClrArray<T> extends ReadonlyArray<T>", global);
    }

    public sealed class TagHolder
    {
        public string[] Tags { get; set; } = [];
        public List<string> TagList { get; set; } = [];
        public string[] GetTags() => Tags;
        public string Join(params string[] parts) => string.Join("|", parts);
    }
}
