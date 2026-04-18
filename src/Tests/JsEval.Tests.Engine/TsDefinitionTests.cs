using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.TsDefinition;
using Cocoar.JsEval.TsDefinition.Definitions;
using Cocoar.JsEval.Module.Common;
using Cocoar.JsEval.Module.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

public class TsDefinitionTests
{
    private readonly ITestOutputHelper _output;

    public TsDefinitionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // --- Type Mapping Tests ---

    [Theory]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(int), "number")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(long), "number")]
    [InlineData(typeof(decimal), "number")]
    [InlineData(typeof(short), "number")]
    [InlineData(typeof(byte), "number")]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(char), "string")]
    [InlineData(typeof(void), "void")]
    [InlineData(typeof(object), "any")]
    [InlineData(typeof(DateTime), "Date")]
    [InlineData(typeof(DateTimeOffset), "Date")]
    [InlineData(typeof(TimeSpan), "number")]
    public void PrimitiveTypes_MapCorrectly(Type netType, string expectedTs)
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(netType);
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal(expectedTs, result);
    }

    [Fact]
    public void ByteArray_MapsToArrayBuffer()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(byte[]));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("ArrayBuffer", result);
    }

    [Fact]
    public void Task_MapsToPromiseVoid()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(Task));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Promise<void>", result);
    }

    [Fact]
    public void ValueTask_MapsToPromiseVoid()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(ValueTask));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Promise<void>", result);
    }

    [Fact]
    public void TaskOfString_MapsToPromiseString()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(Task<string>));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Promise<string>", result);
    }

    [Fact]
    public void TaskOfInt_MapsToPromiseNumber()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(Task<int>));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Promise<number>", result);
    }

    [Fact]
    public void ValueTaskOfBool_MapsToPromiseBoolean()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(ValueTask<bool>));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Promise<boolean>", result);
    }

    // --- Collections stay as .NET types (NOT mapped to Array/Record) ---

    [Fact]
    public void ListOfString_IsNotMappedToArray()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(List<string>));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.DoesNotContain("Array", result);
    }

    [Fact]
    public void DictionaryStringString_IsNotMappedToRecord()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(Dictionary<string, string>));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.DoesNotContain("Record", result);
    }

    // --- TsDefinitionService Integration Tests ---

    [Fact]
    public void GetTsDefinitions_WithModules_ReturnsDefinitions()
    {
        var sp = BuildServiceProvider();
        var moduleRegistry = sp.GetRequiredService<IJsModuleRegistry>();
        var service = new TsDefinitionService(moduleRegistry);

        var definitions = service.GetTsDefinitions();

        Assert.NotEmpty(definitions);
        _output.WriteLine($"Generated {definitions.Count} definition files:");
        foreach (var (key, value) in definitions)
            _output.WriteLine($"  {key} ({value.Length} chars)");
    }

    [Fact]
    public void GetTsDefinitions_ContainsGlobalDts()
    {
        var sp = BuildServiceProvider();
        var moduleRegistry = sp.GetRequiredService<IJsModuleRegistry>();
        var service = new TsDefinitionService(moduleRegistry);

        var definitions = service.GetTsDefinitions();

        Assert.True(definitions.ContainsKey("global.d.ts"));
        var global = definitions["global.d.ts"];

        Assert.Contains("fetch", global);
        Assert.Contains("fetchOptions", global);
        Assert.Contains("NewObject", global);
        Assert.Contains("exit", global);
        Assert.Contains("require", global);
    }

    [Fact]
    public void GetTsDefinitions_ContainsLibFiles()
    {
        var sp = BuildServiceProvider();
        var moduleRegistry = sp.GetRequiredService<IJsModuleRegistry>();
        var service = new TsDefinitionService(moduleRegistry);

        var definitions = service.GetTsDefinitions();

        Assert.True(definitions.ContainsKey("lib.es5.d.ts"));
        Assert.True(definitions.ContainsKey("lib.es2015.core.d.ts"));
    }

    [Fact]
    public void GetTsImports_WithModules_ReturnsModuleExports()
    {
        var sp = BuildServiceProvider();
        var moduleRegistry = sp.GetRequiredService<IJsModuleRegistry>();
        var service = new TsDefinitionService(moduleRegistry);

        var imports = service.GetTsImports();

        Assert.NotEmpty(imports);
        _output.WriteLine($"Generated {imports.Count} import files:");
        foreach (var (key, value) in imports)
        {
            _output.WriteLine($"\n--- {key} ---");
            _output.WriteLine(value);
        }
    }

    [Fact]
    public void GetTsImports_CommonModule_HasExports()
    {
        var sp = BuildServiceProvider();
        var moduleRegistry = sp.GetRequiredService<IJsModuleRegistry>();
        var service = new TsDefinitionService(moduleRegistry);

        var imports = service.GetTsImports();

        Assert.True(imports.ContainsKey("common.ts"));
        var common = imports["common.ts"];

        // CommonModule exposes properties: Guid, Sleep, Random
        Assert.Contains("export const", common);
    }

    [Fact]
    public void GetTsDefinitions_IsCached()
    {
        var sp = BuildServiceProvider();
        var moduleRegistry = sp.GetRequiredService<IJsModuleRegistry>();
        var service = new TsDefinitionService(moduleRegistry);

        var first = service.GetTsDefinitions();
        var second = service.GetTsDefinitions();

        // Same content (separate dictionary instances, but same data)
        Assert.Equal(first.Count, second.Count);
        foreach (var key in first.Keys)
            Assert.Equal(first[key], second[key]);
    }

    // --- DefinitionBuilder Tests ---

    [Fact]
    public void DefinitionBuilder_FromType_ResolvesProperties()
    {
        var td = TypeDefinition.FromType(typeof(SampleClass));

        Assert.Equal("SampleClass", td.Name);
        Assert.True(td.Properties.Count > 0);
        Assert.Contains(td.Properties, p => p.Name == "Name");
        Assert.Contains(td.Properties, p => p.Name == "Count");
    }

    [Fact]
    public void DefinitionBuilder_FromType_ResolvesMethods()
    {
        var td = TypeDefinition.FromType(typeof(SampleClass));

        Assert.True(td.Methods.Count > 0);
        Assert.Contains(td.Methods, m => m.Name == "GetData");
    }

    [Fact]
    public void DefinitionBuilder_FromType_Enum_HasValues()
    {
        var td = TypeDefinition.FromType(typeof(SampleEnum));

        Assert.Equal("enum", td.Kind);
        Assert.Equal(3, td.EnumValueDefinitions.Count);
        Assert.Contains(td.EnumValueDefinitions, e => e.Name == "First");
        Assert.Contains(td.EnumValueDefinitions, e => e.Name == "Second");
        Assert.Contains(td.EnumValueDefinitions, e => e.Name == "Third");
    }

    [Fact]
    public void DefinitionBuilder_Render_ProducesOutput()
    {
        var builder = new DefinitionBuilder();
        builder.AddTypes(typeof(SampleClass));

        var result = builder.Render();

        Assert.NotEmpty(result);
        _output.WriteLine($"Rendered {result.Count} files:");
        foreach (var (key, value) in result)
        {
            _output.WriteLine($"\n--- {key} ---");
            _output.WriteLine(value);
        }
    }

    // --- Helper ---

    private static ServiceProvider BuildServiceProvider()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b
            .AddModule<CommonModule>()
            .AddModule<HttpModule>()
        );
        return sc.BuildServiceProvider();
    }

    // --- Test Types ---

    public class SampleClass
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public List<string> Items { get; set; } = [];
        public Dictionary<string, object> Metadata { get; set; } = [];

        public Task<string> GetData() => Task.FromResult("test");
        public void DoSomething(int value, string name) { }
    }

    public enum SampleEnum
    {
        First = 0,
        Second = 1,
        Third = 2
    }
}
