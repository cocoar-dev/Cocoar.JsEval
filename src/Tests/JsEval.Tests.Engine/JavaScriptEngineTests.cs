using System;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Module.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

public class JavaScriptEngineTests
{
    private IServiceProvider ServiceProvider { get; }

    public JavaScriptEngineTests()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b
            .AddModule<CommonModule>()
        );

        ServiceProvider = sc.BuildServiceProvider();
    }

    [Fact]
    public async Task ExecuteScript_BasicArithmetic_ReturnsCorrectResult()
    {
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync("export const result = 2 + 3;");
        var result = jsEngine.GetValue<int>("result");

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task SetValue_And_GetValue_RoundTrips()
    {
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        jsEngine.SetValue("input", "Hello");
        await jsEngine.ExecuteAsync("export const output = input + ' World';");
        var result = jsEngine.GetValue<string>("output");

        Assert.Equal("Hello World", result);
    }

    [Fact]
    public async Task GetFunction_And_Invoke()
    {
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync(@"
export function add(a, b) {
    return a + b;
}
");

        var func = jsEngine.GetFunction("add");
        Assert.NotNull(func);
        Assert.Equal("add", func.Name);

        var result = jsEngine.InvokeFunction("add", 10, 20);
        Assert.Equal(30d, Convert.ToDouble(result));
    }

    [Fact]
    public async Task JsonParse_And_Stringify()
    {
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        var json = """{"name":"Test","value":42}""";
        var parsed = jsEngine.JsonParse(json);
        Assert.NotNull(parsed);

        var stringified = jsEngine.JsonStringify(parsed);
        Assert.Contains("Test", stringified);
        Assert.Contains("42", stringified);
    }

    [Fact]
    public async Task ModuleImport_CommonModule_Works()
    {
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync(@"
import * as common from 'common'

export const guid = common.Guid.New();
");

        var guid = jsEngine.GetValueAsJson("guid");
        Assert.NotNull(guid);
        Assert.NotEqual("null", guid);
    }

    [Fact]
    public async Task Stop_CancelsExecution()
    {
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        jsEngine.Stop();

        // After calling Stop, executing should not throw unhandled exceptions
        // The engine catches ExecutionCanceledException internally
        await jsEngine.ExecuteAsync("export const x = 1;");
    }

    [Fact]
    public async Task GetValueAsJson_ReturnsValidJson()
    {
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync("export const obj = { name: 'test', count: 5 };");

        var json = jsEngine.GetValueAsJson("obj");
        Assert.Contains("test", json);
        Assert.Contains("5", json);
    }
}
