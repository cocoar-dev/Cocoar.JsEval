using System;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// Tests to verify async/await behavior in the JavaScript and TypeScript engines.
/// These explore what works and what doesn't with Jint 4.8's async support.
/// </summary>
public class AsyncAwaitTests
{
    private readonly ITestOutputHelper _output;

    public AsyncAwaitTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Test: Can we use async functions and await inside ES modules?
    /// This tests Jint's native async/await support in Jint.
    /// </summary>
    [Fact]
    public async Task AsyncFunction_InModule_Works()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        var sp = sc.BuildServiceProvider();

        using var jsEngine = sp.GetRequiredService<JsEngine>();

        // Simple async function that resolves immediately
        await jsEngine.ExecuteAsync(@"
async function fetchData() {
    return 42;
}

export const result = await fetchData();
");

        var result = jsEngine.GetValue<int>("result");
        _output.WriteLine($"result = {result}");
        Assert.Equal(42, result);
    }

    /// <summary>
    /// Test: Does Promise.resolve work?
    /// </summary>
    [Fact]
    public async Task PromiseResolve_Works()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        var sp = sc.BuildServiceProvider();

        using var jsEngine = sp.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync(@"
const p = Promise.resolve('hello');
export const result = await p;
");

        var result = jsEngine.GetValue<string>("result");
        _output.WriteLine($"result = {result}");
        Assert.Equal("hello", result);
    }

    /// <summary>
    /// Test: Can we chain async operations?
    /// </summary>
    [Fact]
    public async Task AsyncChaining_Works()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        var sp = sc.BuildServiceProvider();

        using var jsEngine = sp.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync(@"
async function step1() {
    return 10;
}

async function step2(x) {
    return x * 2;
}

async function step3(x) {
    return x + 5;
}

const v1 = await step1();
const v2 = await step2(v1);
export const result = await step3(v2);
");

        var result = jsEngine.GetValue<int>("result");
        _output.WriteLine($"result = {result}");
        Assert.Equal(25, result); // 10 * 2 + 5
    }

    /// <summary>
    /// Test: Does Promise.all work?
    /// </summary>
    [Fact]
    public async Task PromiseAll_Works()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        var sp = sc.BuildServiceProvider();

        using var jsEngine = sp.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync(@"
const results = await Promise.all([
    Promise.resolve(1),
    Promise.resolve(2),
    Promise.resolve(3)
]);
export const sum = results[0] + results[1] + results[2];
");

        var sum = jsEngine.GetValue<int>("sum");
        _output.WriteLine($"sum = {sum}");
        Assert.Equal(6, sum);
    }

    /// <summary>
    /// Test: Async/await with try/catch error handling
    /// </summary>
    [Fact]
    public async Task AsyncTryCatch_Works()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        var sp = sc.BuildServiceProvider();

        using var jsEngine = sp.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync(@"
async function failing() {
    throw new Error('expected error');
}

let caught = '';
try {
    await failing();
} catch (e) {
    caught = e.message;
}

export const errorMessage = caught;
");

        var msg = jsEngine.GetValue<string>("errorMessage");
        _output.WriteLine($"errorMessage = {msg}");
        Assert.Equal("expected error", msg);
    }

    /// <summary>
    /// Test: Async arrow functions
    /// </summary>
    [Fact]
    public async Task AsyncArrowFunction_Works()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        var sp = sc.BuildServiceProvider();

        using var jsEngine = sp.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync(@"
const double = async (x) => x * 2;
export const result = await double(21);
");

        var result = jsEngine.GetValue<int>("result");
        _output.WriteLine($"result = {result}");
        Assert.Equal(42, result);
    }

    /// <summary>
    /// Test: for-await-of with async iterables
    /// </summary>
    [Fact]
    public async Task ForAwaitOf_Works()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        var sp = sc.BuildServiceProvider();

        using var jsEngine = sp.GetRequiredService<JsEngine>();

        await jsEngine.ExecuteAsync(@"
async function* asyncRange(start, end) {
    for (let i = start; i <= end; i++) {
        yield i;
    }
}

let total = 0;
for await (const num of asyncRange(1, 5)) {
    total += num;
}

export const result = total;
");

        var result = jsEngine.GetValue<int>("result");
        _output.WriteLine($"result = {result}");
        Assert.Equal(15, result); // 1+2+3+4+5
    }
}
