using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Cocoar.JsEval.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace JsEval.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ValueBenchmarks
{
    private ServiceProvider _serviceProvider = null!;
    private JsEngine? _engine;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        _serviceProvider = sc.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _serviceProvider.Dispose();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _engine = _serviceProvider.GetRequiredService<JsEngine>();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _engine?.Dispose();
        _engine = null;
    }

    // --- SetValue benchmarks ---

    [Benchmark(Description = "SetValue with string")]
    public void SetString()
    {
        _engine!.SetValue("str", "Hello, World!");
    }

    [Benchmark(Description = "SetValue with int (boxed as double)")]
    public void SetInt()
    {
        _engine!.SetValue("num", 42.0);
    }

    [Benchmark(Description = "SetValue with bool")]
    public void SetBool()
    {
        _engine!.SetValue("flag", true);
    }

    [Benchmark(Description = "SetValue with complex object")]
    public void SetObject()
    {
        _engine!.SetValue("obj", new { Name = "Test", Value = 42, Active = true, Tags = new[] { "a", "b", "c" } });
    }

    // --- GetValue benchmarks ---

    [Benchmark(Description = "GetValue<string> after setting")]
    public async Task<string?> GetString()
    {
        _engine!.SetValue("str", "Hello, World!");
        await _engine.ExecuteAsync("export const outStr = str;");
        return _engine.GetValue<string>("outStr");
    }

    [Benchmark(Description = "GetValue<int> after execution")]
    public async Task<int?> GetInt()
    {
        await _engine!.ExecuteAsync("export const num = 42;");
        return _engine.GetValue<int>("num");
    }

    [Benchmark(Description = "GetValue<bool> after execution")]
    public async Task<bool?> GetBool()
    {
        await _engine!.ExecuteAsync("export const flag = true;");
        return _engine.GetValue<bool>("flag");
    }

    // --- JSON serialization ---

    [Benchmark(Description = "GetValueAsJson serialization path")]
    public async Task<string> GetValueAsJson()
    {
        await _engine!.ExecuteAsync("export const obj = { name: 'test', count: 5, items: [1, 2, 3] };");
        return _engine.GetValueAsJson("obj");
    }

    // --- Task interop ---

    [Benchmark(Description = ".NET Task to JS Promise conversion")]
    public async Task<int?> TaskInterop()
    {
        _engine!.SetValue("asyncAdd", new Func<int, int, Task<int>>(async (a, b) =>
        {
            await Task.CompletedTask;
            return a + b;
        }));

        await _engine.ExecuteAsync(@"
export const result = await asyncAdd(10, 20);
");
        return _engine.GetValue<int>("result");
    }
}
