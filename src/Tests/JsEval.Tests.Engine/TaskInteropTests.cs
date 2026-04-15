using System;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// Tests that verify .NET Task -> JS Promise interop (TaskInterop feature).
/// Scripts can await .NET async methods directly.
/// </summary>
public class TaskInteropTests
{
    private readonly ITestOutputHelper _output;

    public TaskInteropTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private JsEngine CreateEngine()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        var sp = sc.BuildServiceProvider();
        return sp.GetRequiredService<JsEngine>();
    }

    [Fact]
    public async Task AwaitDotNetTask_ReturnsResult()
    {
        using var jsEngine = CreateEngine();

        // Expose a .NET async method that returns Task<string>
        jsEngine.SetValue("fetchMessage", new Func<Task<string>>(async () =>
        {
            await Task.Delay(10);
            return "Hello from .NET async!";
        }));

        await jsEngine.ExecuteAsync(@"
export const result = await fetchMessage();
");

        var result = jsEngine.GetValue<string>("result");
        _output.WriteLine($"result = {result}");
        Assert.Equal("Hello from .NET async!", result);
    }

    [Fact]
    public async Task AwaitDotNetTask_WithParameter()
    {
        using var jsEngine = CreateEngine();

        jsEngine.SetValue("multiply", new Func<int, int, Task<int>>(async (a, b) =>
        {
            await Task.Delay(5);
            return a * b;
        }));

        await jsEngine.ExecuteAsync(@"
export const result = await multiply(6, 7);
");

        var result = jsEngine.GetValue<int>("result");
        _output.WriteLine($"result = {result}");
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task AwaitMultipleDotNetTasks_WithPromiseAll()
    {
        using var jsEngine = CreateEngine();

        jsEngine.SetValue("getNumber", new Func<int, Task<int>>(async (n) =>
        {
            await Task.Delay(5);
            return n * 10;
        }));

        await jsEngine.ExecuteAsync(@"
const results = await Promise.all([
    getNumber(1),
    getNumber(2),
    getNumber(3)
]);
export const sum = results[0] + results[1] + results[2];
");

        var sum = jsEngine.GetValue<int>("sum");
        _output.WriteLine($"sum = {sum}");
        Assert.Equal(60, sum); // 10 + 20 + 30
    }

    [Fact]
    public async Task AwaitDotNetTask_ThenChaining()
    {
        using var jsEngine = CreateEngine();

        jsEngine.SetValue("getValue", new Func<Task<int>>(async () =>
        {
            await Task.Delay(5);
            return 21;
        }));

        await jsEngine.ExecuteAsync(@"
const doubled = await getValue().then(v => v * 2);
export const result = doubled;
");

        var result = jsEngine.GetValue<int>("result");
        _output.WriteLine($"result = {result}");
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task AwaitDotNetTask_ErrorHandling()
    {
        using var jsEngine = CreateEngine();

        jsEngine.SetValue("failingMethod", new Func<Task<string>>(async () =>
        {
            await Task.Delay(5);
            throw new InvalidOperationException("Something went wrong");
        }));

        await jsEngine.ExecuteAsync(@"
let errorMsg = '';
try {
    await failingMethod();
} catch (e) {
    errorMsg = e.message || e.toString();
}
export const result = errorMsg;
");

        var result = jsEngine.GetValue<string>("result");
        _output.WriteLine($"error = {result}");
        Assert.Contains("Something went wrong", result);
    }

    [Fact]
    public async Task AwaitDotNetValueTask_Works()
    {
        using var jsEngine = CreateEngine();

        jsEngine.SetValue("quickCalc", new Func<int, ValueTask<int>>(async (n) =>
        {
            await Task.Delay(1);
            return n * n;
        }));

        await jsEngine.ExecuteAsync(@"
export const result = await quickCalc(9);
");

        var result = jsEngine.GetValue<int>("result");
        _output.WriteLine($"result = {result}");
        Assert.Equal(81, result);
    }

    [Fact]
    public async Task MixAsyncAndSync_InSameScript()
    {
        using var jsEngine = CreateEngine();

        jsEngine.SetValue("asyncAdd", new Func<int, int, Task<int>>(async (a, b) =>
        {
            await Task.Delay(5);
            return a + b;
        }));

        await jsEngine.ExecuteAsync(@"
// Sync computation
const x = 10;
const y = 20;
const syncResult = x + y;

// Async computation
const asyncResult = await asyncAdd(x, y);

// Both should be equal
export const same = syncResult === asyncResult;
export const value = asyncResult;
");

        Assert.True(jsEngine.GetValue<bool>("same"));
        Assert.Equal(30, jsEngine.GetValue<int>("value"));
    }
}
