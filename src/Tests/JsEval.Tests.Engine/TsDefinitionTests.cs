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

    // For generic Task<T>/ValueTask<T>, NormalizeTypeName returns the bare
    // wrapper ("Promise"); the caller (BuildTypeString / GetTypeString /
    // BuildTypeDefinitionTypeString) appends generic arguments from the
    // TypeDefinition. See the full-render assertions further below for the
    // end-to-end "Promise<string>" output.

    [Fact]
    public void TaskOfString_MapsToPromiseBare()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(Task<string>));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Promise", result);
    }

    [Fact]
    public void TaskOfInt_MapsToPromiseBare()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(Task<int>));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Promise", result);
    }

    [Fact]
    public void ValueTaskOfBool_MapsToPromiseBare()
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(typeof(ValueTask<bool>));
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Promise", result);
    }

    // --- Collections → Array / ReadonlyArray / Record ---

    [Theory]
    [InlineData(typeof(List<string>))]
    [InlineData(typeof(IList<string>))]
    [InlineData(typeof(IEnumerable<string>))]
    [InlineData(typeof(ICollection<string>))]
    [InlineData(typeof(HashSet<string>))]
    [InlineData(typeof(ISet<string>))]
    public void ArrayLikeCollections_MapsToArrayBare(Type collectionType)
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(collectionType);
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Array", result);
    }

    [Theory]
    [InlineData(typeof(IReadOnlyList<string>))]
    [InlineData(typeof(IReadOnlyCollection<string>))]
    public void ReadOnlyCollections_MapsToReadonlyArrayBare(Type collectionType)
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(collectionType);
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("ReadonlyArray", result);
    }

    [Theory]
    [InlineData(typeof(Dictionary<string, string>))]
    [InlineData(typeof(IDictionary<string, string>))]
    [InlineData(typeof(IReadOnlyDictionary<string, string>))]
    public void Dictionaries_MapsToRecordBare(Type dictionaryType)
    {
        var defaults = new TypeScriptRendererDefaults();
        var typeDef = TypeDefinition.FromType(dictionaryType);
        var result = defaults.NormalizeTypeName(typeDef, []);

        Assert.Equal("Record", result);
    }

    [Fact]
    public void Render_ListProperty_EmitsArrayGeneric()
    {
        var builder = new DefinitionBuilder();
        builder.AddTypes(typeof(SampleClass));

        var rendered = string.Join("\n", builder.Render().Values);

        Assert.Contains("Array<string>", rendered);
    }

    [Fact]
    public void Render_DictionaryProperty_EmitsRecordGeneric()
    {
        var builder = new DefinitionBuilder();
        builder.AddTypes(typeof(SampleClass));

        var rendered = string.Join("\n", builder.Render().Values);

        Assert.Contains("Record<string,", rendered);
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
        Assert.Contains("require", global);
        // exit() removed in 4.0 — see SECURITY.md (no replacement; use IIFE
        // for early-return: `(() => { if (cond) return early; ... })()`).
        Assert.DoesNotContain("function exit(", global);
    }

    // The package no longer ships its own lib.*.d.ts files. Monaco's TypeScript
    // language service loads its own (version-matched) libs; Cocoar.JsEval.TypeScript
    // and Cocoar.JsEval.TypeScript.V8 embed authoritative TS 6.0 libs for their
    // own transpilation/type-check paths. Shipping a second, stale set from here
    // only caused version-mismatch surprises.
    [Fact]
    public void GetTsDefinitions_DoesNotShipLibFiles()
    {
        var sp = BuildServiceProvider();
        var moduleRegistry = sp.GetRequiredService<IJsModuleRegistry>();
        var service = new TsDefinitionService(moduleRegistry);

        var definitions = service.GetTsDefinitions();

        Assert.DoesNotContain("lib.es5.d.ts", definitions.Keys);
        Assert.DoesNotContain("lib.es2015.core.d.ts", definitions.Keys);
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

    // --- Regression tests for TsDefinition renderer bugs ---

    // `Task<T>` / `ValueTask<T>` returned a name with the generic arg already baked in
    // (e.g. "Promise<string>") while the caller appended the args a second time,
    // producing "Promise<string><string>" — a parse error in TypeScript.
    [Fact]
    public void Render_TaskOfT_DoesNotProduceDoubleGenerics()
    {
        var builder = new DefinitionBuilder();
        builder.AddTypes(typeof(SampleClass));

        var rendered = string.Join("\n", builder.Render().Values);

        Assert.Contains("Promise<string>", rendered);
        Assert.DoesNotContain("Promise<string><", rendered);
        Assert.DoesNotContain("><", rendered);
    }

    // `ref T` return types / parameters leaked the .NET ByRef name suffix '&'
    // into the rendered output (e.g. "Current: T&"), which is a parse error in
    // TypeScript (intersection operator without a right operand).
    [Fact]
    public void Render_RefReturn_DoesNotLeakAmpersand()
    {
        var builder = new DefinitionBuilder();
        builder.AddTypes(typeof(RefReturningSample));

        var rendered = string.Join("\n", builder.Render().Values);

        Assert.DoesNotContain("&;", rendered);
        Assert.DoesNotContain("&>", rendered);
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

    public class RefReturningSample
    {
        private int _value;
        public ref int GetRef() => ref _value;
        public ref readonly int GetReadOnlyRef() => ref _value;
    }

    // --- Test types for alias / MapNamespace / collision tests ---

    public class FirstHolder { public SecondHolder Nested { get; set; } = new(); }
    public class SecondHolder { public string Value { get; set; } = ""; }

    // --- Tests for v3.1.4 additions: short-name aliases + MapNamespace ---

    [Fact]
    public void AddType_WithAlias_EmitsAtRootScope()
    {
        var builder = new DefinitionBuilder();
        builder.AddType(typeof(FirstHolder), alias: "FirstHolder");

        var rendered = string.Join("\n", builder.Render().Values);

        // Root-scope emission: no namespace wrapper, just `declare interface FirstHolder`.
        Assert.Contains("FirstHolder", rendered);
        Assert.DoesNotContain("namespace JsEval.Tests.Engine.TsDefinitionTests", rendered);
    }

    [Fact]
    public void AddType_WithAlias_CrossReferenceUsesShortName()
    {
        var builder = new DefinitionBuilder();
        builder.AddType(typeof(FirstHolder), alias: "FirstHolder");
        builder.AddType(typeof(SecondHolder), alias: "SecondHolder");

        var rendered = string.Join("\n", builder.Render().Values);

        // FirstHolder's `Nested: SecondHolder` cross-ref should use the short name,
        // not the fully-qualified JsEval.Tests.Engine.TsDefinitionTests.SecondHolder.
        Assert.Contains("Nested: SecondHolder", rendered);
        Assert.DoesNotContain("TsDefinitionTests.SecondHolder", rendered);
    }

    [Fact]
    public void AddType_DuplicateAliasForDifferentTypes_Throws()
    {
        var builder = new DefinitionBuilder();
        builder.AddType(typeof(FirstHolder), alias: "Holder");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddType(typeof(SecondHolder), alias: "Holder"));

        Assert.Contains("Holder", ex.Message);
        Assert.Contains("FirstHolder", ex.Message);
        Assert.Contains("SecondHolder", ex.Message);
    }

    [Fact]
    public void MapNamespace_FlattensToRootScope()
    {
        var builder = new DefinitionBuilder();
        builder.MapNamespace("JsEval.Tests.Engine", "");
        builder.AddTypes(typeof(FirstHolder));

        var rendered = string.Join("\n", builder.Render().Values);

        // The type's original deep namespace (TsDefinitionTests is nested) collapses
        // to root; we should see `FirstHolder` without the long namespace wrapper.
        Assert.Contains("FirstHolder", rendered);
        Assert.DoesNotContain("namespace JsEval.Tests.Engine", rendered);
    }

    // Regression: MapNamespace("X", "") used to only strip the prefix, leaving
    // sub-namespaces intact — so a type in X.Sub landed in namespace `Sub`
    // instead of at root. Empty target is now a *full flatten*: every type
    // under the source prefix, at any depth, lands at root scope.
    [Fact]
    public void MapNamespace_EmptyTarget_FullyFlattensDeepSubNamespaces()
    {
        var builder = new DefinitionBuilder();
        builder.MapNamespace("JsEval.Tests.Engine", "");
        // Deep type (inside TsDefinitionTests which is nested in JsEval.Tests.Engine.TsDefinitionTests).
        builder.AddTypes(typeof(FirstHolder));

        var files = builder.Render();
        var rendered = string.Join("\n", files.Values);

        // Root bucket is 'globals.d.ts'; there should be no 'TsDefinitionTests.d.ts'
        // or similar "sub-namespace only had its prefix stripped" artifact.
        Assert.True(files.ContainsKey("globals.d.ts"));
        Assert.DoesNotContain("TsDefinitionTests.d.ts", files.Keys);
        Assert.DoesNotContain("declare namespace TsDefinitionTests", rendered);
    }

    // Non-empty target keeps the "strip + prepend" semantics so consumers who
    // want to disambiguate between multiple source trees can re-home them under
    // a shared shorter prefix.
    [Fact]
    public void MapNamespace_NonEmptyTarget_StripsAndPrepends()
    {
        var builder = new DefinitionBuilder();
        builder.MapNamespace("JsEval.Tests.Engine", "Short");
        builder.AddTypes(typeof(FirstHolder));

        var rendered = string.Join("\n", builder.Render().Values);

        // `JsEval.Tests.Engine.TsDefinitionTests.FirstHolder` → `Short.TsDefinitionTests.FirstHolder`
        Assert.Contains("namespace Short", rendered);
    }

    [Fact]
    public void MapNamespace_DoesNotAffectSystemTypes()
    {
        var builder = new DefinitionBuilder();
        builder.MapNamespace("", ""); // Try to flatten everything — should NOT affect System.*
        builder.AddTypes(typeof(FirstHolder));

        var rendered = string.Join("\n", builder.Render().Values);

        // System.* stays fully qualified even with a greedy MapNamespace rule.
        Assert.Contains("System", rendered);
    }

    // NewObject must resolve through the JsEngineOptions.TypeAliases map so the
    // same short name used in .d.ts works at runtime.
    [Fact]
    public void AddTypeAlias_NewObjectResolvesShortName()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b
            .EnableNewObject()
            .AddTypeAlias<FirstHolder>("FirstHolder"));
        using var sp = sc.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();

        // NewObject("FirstHolder") must hit the alias map and return a real FirstHolder instance.
        var result = engine.EvaluateExpression("NewObject('FirstHolder')");
        var instance = result.ToObject();
        Assert.IsType<FirstHolder>(instance);
    }
}
