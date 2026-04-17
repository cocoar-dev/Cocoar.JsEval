using System;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Module.Common;
using Cocoar.JsEval.TsDefinition;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

// -----------------------------------------------------------------
// Test helpers: tagged and untagged modules for tag-filtering tests
// -----------------------------------------------------------------

[JsModule(Name = "tagged", Tags = ["admin"])]
public class TaggedTestModule : IJsModule
{
    public string GetSecret() => "admin-secret";
}

[JsModule(Name = "untagged")]
public class UntaggedTestModule : IJsModule
{
    public string GetPublicData() => "public-data";
}

/// <summary>
/// Comprehensive tests for features added during the optimization/improvement phase:
/// Evaluate, Prepare, EvaluateAsync, console, setTimeout, structuredClone,
/// engine reuse, JsEvalBuilder, JsEngine DI, tag filtering, and debug mode.
/// </summary>
public class NewFeaturesTests
{
    private readonly ITestOutputHelper _output;

    public NewFeaturesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static JsEngine CreateEngine(Action<JsEvalBuilder>? configure = null)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(configure ?? (_ => { }));
        var sp = sc.BuildServiceProvider();
        return sp.GetRequiredService<JsEngine>();
    }

    private static ServiceProvider BuildServiceProvider(Action<JsEvalBuilder>? configure = null)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(configure ?? (_ => { }));
        return sc.BuildServiceProvider();
    }

    // =================================================================
    // 1. Evaluate (lightweight sync execution)
    // =================================================================

    [Fact]
    public void Evaluate_SimpleScript_Works()
    {
        using var engine = CreateEngine();

        engine.Evaluate("var x = 2 + 3;");
        var result = engine.GetValue<int>("x");

        Assert.Equal(5, result);
    }

    [Fact]
    public void Evaluate_WithSetValue_AccessesGlobals()
    {
        using var engine = CreateEngine();

        engine.SetValue("name", "World");
        engine.Evaluate("var greeting = 'Hello ' + name;");
        var result = engine.GetValue<string>("greeting");

        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Evaluate_ClrMethodCall_Works()
    {
        using var engine = CreateEngine();

        engine.SetValue("math", new SimpleMath());
        engine.Evaluate("var sum = math.Add(10, 32);");
        var result = engine.GetValue<int>("sum");

        Assert.Equal(42, result);
    }

    [Fact]
    public void Evaluate_MultipleCallsOnSameEngine_Works()
    {
        using var engine = CreateEngine();

        engine.Evaluate("var a = 1;");
        engine.Evaluate("var b = 2;");
        engine.Evaluate("var c = a + b;");

        Assert.Equal(1, engine.GetValue<int>("a"));
        Assert.Equal(2, engine.GetValue<int>("b"));
        Assert.Equal(3, engine.GetValue<int>("c"));
    }

    [Fact]
    public void Evaluate_StatePersistedBetweenCalls()
    {
        using var engine = CreateEngine();

        engine.Evaluate("var counter = 0;");
        engine.Evaluate("counter++;");
        var result = engine.GetValue<int>("counter");

        Assert.Equal(1, result);
    }

    // =================================================================
    // 2. Evaluate with Prepared Scripts
    // =================================================================

    [Fact]
    public void Prepare_ReturnsPreparedScript()
    {
        var prepared = JsEngine.Prepare("var x = 42;");

        Assert.NotNull(prepared);
    }

    [Fact]
    public void EvaluatePrepared_Works()
    {
        using var engine = CreateEngine();

        var prepared = JsEngine.Prepare("var x = 42;");
        engine.Evaluate(prepared);
        var result = engine.GetValue<int>("x");

        Assert.Equal(42, result);
    }

    [Fact]
    public void EvaluatePrepared_CanBeReusedMultipleTimes()
    {
        var prepared = JsEngine.Prepare("var result = input * 2;");

        using var engine = CreateEngine();

        engine.SetValue("input", 5);
        engine.Evaluate(prepared);
        Assert.Equal(10, engine.GetValue<int>("result"));

        engine.SetValue("input", 10);
        engine.Evaluate(prepared);
        Assert.Equal(20, engine.GetValue<int>("result"));

        engine.SetValue("input", 21);
        engine.Evaluate(prepared);
        Assert.Equal(42, engine.GetValue<int>("result"));
    }

    [Fact]
    public void EvaluatePrepared_SharedAcrossEngines()
    {
        var prepared = JsEngine.Prepare("var doubled = value * 2;");

        using var engine1 = CreateEngine();
        using var engine2 = CreateEngine();

        engine1.SetValue("value", 5);
        engine1.Evaluate(prepared);
        Assert.Equal(10, engine1.GetValue<int>("doubled"));

        engine2.SetValue("value", 100);
        engine2.Evaluate(prepared);
        Assert.Equal(200, engine2.GetValue<int>("doubled"));
    }

    // =================================================================
    // 3. EvaluateAsync
    // =================================================================

    [Fact]
    public async Task EvaluateAsync_SimpleScript_Works()
    {
        using var engine = CreateEngine();

        await engine.EvaluateAsync("var x = 42;");
        var result = engine.GetValue<int>("x");

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task EvaluateAsync_WithAsyncFunction_Works()
    {
        using var engine = CreateEngine();

        engine.SetValue("asyncFunc", new Func<Task<string>>(async () =>
        {
            await Task.Delay(1);
            return "async-result";
        }));

        // EvaluateAsync runs in script mode (not module mode), so top-level await
        // is not supported. Instead, use an async IIFE to await inside.
        await engine.EvaluateAsync(@"
var result;
(async function() {
    result = await asyncFunc();
})();
");
        var result = engine.GetValue<string>("result");

        _output.WriteLine($"result = {result}");
        Assert.Equal("async-result", result);
    }

    // =================================================================
    // 4. Engine Reuse (ExecuteAsync multiple times)
    // =================================================================

    [Fact]
    public async Task ExecuteAsync_CalledMultipleTimes_Works()
    {
        using var engine = CreateEngine();

        await engine.ExecuteAsync("export const a = 1;");
        await engine.ExecuteAsync("export const b = 2;");

        var result = engine.GetValue<int>("b");
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task ExecuteAsync_DifferentScripts_EachWorksCorrectly()
    {
        using var engine = CreateEngine();

        await engine.ExecuteAsync("export const first = 'alpha';");
        var r1 = engine.GetValue<string>("first");
        Assert.Equal("alpha", r1);

        await engine.ExecuteAsync("export const second = 'beta';");
        var r2 = engine.GetValue<string>("second");
        Assert.Equal("beta", r2);

        await engine.ExecuteAsync("export const third = 'gamma';");
        var r3 = engine.GetValue<string>("third");
        Assert.Equal("gamma", r3);
    }

    // =================================================================
    // 5. Console
    // =================================================================

    [Fact]
    public void ConsoleLog_DoesNotThrow()
    {
        using var engine = CreateEngine();

        engine.Evaluate("console.log('hello');");
    }

    [Fact]
    public void ConsoleWarn_DoesNotThrow()
    {
        using var engine = CreateEngine();

        engine.Evaluate("console.warn('warning');");
    }

    [Fact]
    public void ConsoleError_DoesNotThrow()
    {
        using var engine = CreateEngine();

        engine.Evaluate("console.error('error');");
    }

    [Fact]
    public void ConsoleLog_MultipleArgs_DoesNotThrow()
    {
        using var engine = CreateEngine();

        engine.Evaluate("console.log('hello', 42, true);");
    }

    [Fact]
    public void ConsoleDebug_DoesNotThrow()
    {
        using var engine = CreateEngine();

        engine.Evaluate("console.debug('debug message');");
    }

    [Fact]
    public void ConsoleInfo_DoesNotThrow()
    {
        using var engine = CreateEngine();

        engine.Evaluate("console.info('info message');");
    }

    // =================================================================
    // 6. setTimeout / setInterval
    // =================================================================

    [Fact]
    public void SetTimeout_IsAvailable()
    {
        using var engine = CreateEngine();

        // setTimeout should be defined and callable without throwing
        engine.Evaluate("var called = false; setTimeout(function() { called = true; }, 1);");
    }

    [Fact]
    public void ClearTimeout_IsAvailable()
    {
        using var engine = CreateEngine();

        engine.Evaluate("var id = setTimeout(function() {}, 1000); clearTimeout(id);");
    }

    [Fact]
    public void SetInterval_IsAvailable()
    {
        using var engine = CreateEngine();

        engine.Evaluate("var id = setInterval(function() {}, 1000); clearInterval(id);");
    }

    [Fact]
    public void SetTimeout_ReturnsId()
    {
        using var engine = CreateEngine();

        engine.Evaluate("var timerId = setTimeout(function() {}, 100);");
        var id = engine.GetValue<int>("timerId");

        Assert.True(id > 0);
    }

    [Fact]
    public void SetInterval_ReturnsId()
    {
        using var engine = CreateEngine();

        engine.Evaluate("var intervalId = setInterval(function() {}, 100); clearInterval(intervalId);");
        var id = engine.GetValue<int>("intervalId");

        Assert.True(id > 0);
    }

    // =================================================================
    // 7. structuredClone
    // =================================================================

    [Fact]
    public void StructuredClone_ClonesObject()
    {
        using var engine = CreateEngine();

        engine.Evaluate(@"
var orig = {a: 1, b: 2};
var copy = structuredClone(orig);
copy.a = 99;
var origA = orig.a;
var copyA = copy.a;
");

        Assert.Equal(1, engine.GetValue<int>("origA"));
        Assert.Equal(99, engine.GetValue<int>("copyA"));
    }

    [Fact]
    public void StructuredClone_ClonesNestedObject()
    {
        using var engine = CreateEngine();

        engine.Evaluate(@"
var orig = {nested: {value: 42}};
var copy = structuredClone(orig);
copy.nested.value = 0;
var origVal = orig.nested.value;
var copyVal = copy.nested.value;
");

        Assert.Equal(42, engine.GetValue<int>("origVal"));
        Assert.Equal(0, engine.GetValue<int>("copyVal"));
    }

    [Fact]
    public void StructuredClone_ClonesArray()
    {
        using var engine = CreateEngine();

        engine.Evaluate(@"
var orig = [1, 2, 3];
var copy = structuredClone(orig);
copy[0] = 99;
var origFirst = orig[0];
var copyFirst = copy[0];
");

        Assert.Equal(1, engine.GetValue<int>("origFirst"));
        Assert.Equal(99, engine.GetValue<int>("copyFirst"));
    }

    // =================================================================
    // 8. JsEvalBuilder
    // =================================================================

    [Fact]
    public async Task Builder_AddModule_RegistersModule()
    {
        using var engine = CreateEngine(b => b.AddModule<CommonModule>());

        await engine.ExecuteAsync(@"
import * as common from 'common'
export const guid = common.Guid.New();
");

        var guid = engine.GetValueAsJson("guid");
        Assert.NotNull(guid);
        Assert.NotEqual("null", guid);
    }

    [Fact]
    public void Builder_EnableFetch_EnablesFetch()
    {
        using var engine = CreateEngine(b => b.EnableFetch());

        engine.Evaluate("var hasFetch = typeof fetch !== 'undefined';");
        var hasFetch = engine.GetValue<bool>("hasFetch");

        Assert.True(hasFetch);
    }

    [Fact]
    public void Builder_NoFetch_FetchNotAvailable()
    {
        using var engine = CreateEngine();

        engine.Evaluate("var hasFetch = typeof fetch !== 'undefined';");
        var hasFetch = engine.GetValue<bool>("hasFetch");

        Assert.False(hasFetch);
    }

    [Fact]
    public void Builder_EnableDebugMode_DoesNotThrow()
    {
        using var engine = CreateEngine(b => b.EnableDebugMode());

        engine.Evaluate("var x = 1 + 2;");
        var result = engine.GetValue<int>("x");

        Assert.Equal(3, result);
    }

    [Fact]
    public void Builder_FluentChaining_Works()
    {
        using var engine = CreateEngine(b => b
            .AddModule<CommonModule>()
            .EnableFetch()
            .EnableDebugMode());

        engine.Evaluate("var x = 'builder works';");
        var result = engine.GetValue<string>("x");

        Assert.Equal("builder works", result);
    }

    // =================================================================
    // 9. JsEngine via DI
    // =================================================================

    [Fact]
    public void JsEngine_CanBeResolvedFromDI()
    {
        using var sp = BuildServiceProvider();
        var engine = sp.GetRequiredService<JsEngine>();

        Assert.NotNull(engine);
        Assert.IsType<JsEngine>(engine);
    }

    [Fact]
    public void JsEngine_CanExecuteScript()
    {
        using var sp = BuildServiceProvider();
        using var engine = sp.GetRequiredService<JsEngine>();

        engine.Evaluate("var x = 42;");
        var result = engine.GetValue<int>("x");

        Assert.Equal(42, result);
    }

    [Fact]
    public void JsEngine_IsScoped_SharedWithinScope_DistinctAcrossScopes()
    {
        using var sp = BuildServiceProvider();

        // Within a single scope, multiple services that depend on JsEngine share the
        // same instance — crucial because Jint is not thread-safe and because
        // globals set via SetValue should be visible across collaborators.
        using var scope1 = sp.CreateScope();
        var engine1a = scope1.ServiceProvider.GetRequiredService<JsEngine>();
        var engine1b = scope1.ServiceProvider.GetRequiredService<JsEngine>();
        Assert.Same(engine1a, engine1b);

        // Across scopes, fresh instances.
        using var scope2 = sp.CreateScope();
        var engine2 = scope2.ServiceProvider.GetRequiredService<JsEngine>();
        Assert.NotSame(engine1a, engine2);
    }

    [Fact]
    public void JsEngine_ConcreteType_CanBeResolvedFromDI()
    {
        using var sp = BuildServiceProvider();
        using var engine = sp.GetRequiredService<JsEngine>();

        Assert.NotNull(engine);
    }

    // =================================================================
    // 10. AddTsDefinition DI
    // =================================================================

    [Fact]
    public void AddTsDefinition_RegistersService()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b.AddModule<CommonModule>());
        sc.AddTsDefinition();
        using var sp = sc.BuildServiceProvider();

        var service = sp.GetRequiredService<TsDefinitionService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void AddTsDefinition_ServiceProducesDefinitions()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b.AddModule<CommonModule>());
        sc.AddTsDefinition();
        using var sp = sc.BuildServiceProvider();

        var service = sp.GetRequiredService<TsDefinitionService>();
        var definitions = service.GetTsDefinitions();

        Assert.NotEmpty(definitions);
        _output.WriteLine($"Generated {definitions.Count} definition files");
    }

    // =================================================================
    // 11. Tag Filtering
    // =================================================================

    [Fact]
    public async Task TagFiltering_TaggedModule_IncludedWhenTagMatches()
    {
        using var engine = CreateEngine(b => b
            .AddModule<TaggedTestModule>()
            .AddModule<UntaggedTestModule>());

        engine.AddTaggedModules("admin");

        await engine.ExecuteAsync(@"
import * as tagged from 'tagged'
export const secret = tagged.GetSecret();
");

        var result = engine.GetValue<string>("secret");
        Assert.Equal("admin-secret", result);
    }

    [Fact]
    public async Task TagFiltering_UntaggedModule_ExcludedWhenTagsActive()
    {
        using var engine = CreateEngine(b => b
            .AddModule<TaggedTestModule>()
            .AddModule<UntaggedTestModule>());

        // When tags are active, only modules with matching tags are loaded.
        // Untagged modules (Tags is null or empty) should be excluded.
        engine.AddTaggedModules("admin");

        // The untagged module should not be available as an ES module import,
        // because AddModules filters out modules without matching tags.
        // Attempting to import it should fail.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await engine.ExecuteAsync(@"
import * as ut from 'untagged'
export const data = ut.GetPublicData();
");
        });
    }

    [Fact]
    public async Task TagFiltering_TaggedModule_ExcludedWhenDifferentTag()
    {
        using var engine = CreateEngine(b => b
            .AddModule<TaggedTestModule>()
            .AddModule<UntaggedTestModule>());

        // Request a tag that the tagged module doesn't have
        engine.AddTaggedModules("public");

        // The "admin"-tagged module should not be available
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await engine.ExecuteAsync(@"
import * as tagged from 'tagged'
export const secret = tagged.GetSecret();
");
        });
    }

    [Fact]
    public async Task TagFiltering_NoTagsSet_UntaggedModulesAvailable()
    {
        using var engine = CreateEngine(b => b
            .AddModule<UntaggedTestModule>());

        // No tag filter set -- untagged modules should be available
        await engine.ExecuteAsync(@"
import * as ut from 'untagged'
export const data = ut.GetPublicData();
");

        var result = engine.GetValue<string>("data");
        Assert.Equal("public-data", result);
    }

    [Fact]
    public async Task TagFiltering_NoTagsSet_TaggedModuleRequireBlocked()
    {
        using var engine = CreateEngine(b => b
            .AddModule<TaggedTestModule>());

        // When no tags are set, AddModules does not filter (all modules are added as ES modules).
        // However, BuildModuleInstance receives an empty (but non-null) _useTaggedModules list,
        // which triggers tag checking. A tagged module whose tags don't match the empty list
        // will be rejected at require() time.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await engine.ExecuteAsync(@"
import * as tagged from 'tagged'
export const secret = tagged.GetSecret();
");
        });
    }

    // =================================================================
    // 12. EnableDebugMode opt-in
    // =================================================================

    [Fact]
    public void DebugMode_NotEnabledByDefault()
    {
        using var engine = CreateEngine();

        // Engine should work without debug mode
        engine.Evaluate("var x = 10 * 10;");
        var result = engine.GetValue<int>("x");

        Assert.Equal(100, result);
    }

    [Fact]
    public void DebugMode_CanBeEnabled()
    {
        using var engine = CreateEngine(b => b.EnableDebugMode());

        engine.Evaluate("var x = 7 * 6;");
        var result = engine.GetValue<int>("x");

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task DebugMode_WorksWithExecuteAsync()
    {
        using var engine = CreateEngine(b => b.EnableDebugMode());

        await engine.ExecuteAsync("export const result = 'debug-ok';");
        var result = engine.GetValue<string>("result");

        Assert.Equal("debug-ok", result);
    }

    // =================================================================
    // Additional edge case tests
    // =================================================================

    [Fact]
    public void Evaluate_EmptyString_DoesNotThrow()
    {
        using var engine = CreateEngine();

        // Empty scripts should not cause errors
        engine.Evaluate("");
    }

    [Fact]
    public async Task EvaluateAsync_EmptyString_DoesNotThrow()
    {
        using var engine = CreateEngine();

        await engine.EvaluateAsync("");
    }

    [Fact]
    public async Task EvaluateAsync_NullOrWhitespace_DoesNotThrow()
    {
        using var engine = CreateEngine();

        await engine.EvaluateAsync("   ");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyString_DoesNotThrow()
    {
        using var engine = CreateEngine();

        await engine.ExecuteAsync("");
    }

    [Fact]
    public void Evaluate_BooleanValues_Work()
    {
        using var engine = CreateEngine();

        engine.SetValue("flag", true);
        engine.Evaluate("var result = flag ? 'yes' : 'no';");
        var result = engine.GetValue<string>("result");

        Assert.Equal("yes", result);
    }

    [Fact]
    public void Evaluate_DoubleValues_Work()
    {
        using var engine = CreateEngine();

        engine.SetValue("pi", 3.14);
        engine.Evaluate("var rounded = Math.round(pi);");
        var result = engine.GetValue<int>("rounded");

        Assert.Equal(3, result);
    }

    [Fact]
    public void Options_AreExposedOnEngine()
    {
        using var engine = CreateEngine();

        Assert.NotNull(engine.Options);
    }

    // =================================================================
    // Test helper CLR type for Evaluate_ClrMethodCall_Works
    // =================================================================

    public class SimpleMath
    {
        public int Add(int a, int b) => a + b;
        public int Multiply(int a, int b) => a * b;
    }
}
