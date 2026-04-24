using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Module.Common;
using Microsoft.Extensions.DependencyInjection;

namespace JsEval.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class EngineBenchmarks
{
    private ServiceProvider _serviceProvider = null!;
    private ServiceProvider _serviceProviderWithModules = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        _serviceProvider = sc.BuildServiceProvider();

        var scWithModules = new ServiceCollection();
        scWithModules.AddJsEval(b => b
            .AddModule<CommonModule>()
        );
        _serviceProviderWithModules = scWithModules.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _serviceProvider.Dispose();
        _serviceProviderWithModules.Dispose();
    }

    // JsEngine is registered as scoped — resolving from a fresh scope each iteration
    // ensures we actually measure a *new* engine, not a cached one.

    [Benchmark(Description = "Create a new JsEngine (cold-start cost)")]
    public JsEngine EngineCreation()
    {
        using var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<JsEngine>();
    }

    [Benchmark(Description = "Execute 'export const x = 2 + 3' and get value")]
    public async Task<int?> SimpleExpression()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync("export const x = 2 + 3;");
        return engine.GetValue<int>("x");
    }

    [Benchmark(Description = "SetValue + Execute + GetValue round-trip")]
    public async Task<string?> SetAndGetValue()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        engine.SetValue("input", "Hello");
        await engine.ExecuteAsync("export const output = input + ' World';");
        return engine.GetValue<string>("output");
    }

    [Benchmark(Description = "JsonParse then JsonStringify cycle")]
    public string JsonParseStringify()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        var json = """{"name":"Test","value":42,"nested":{"a":1,"b":2}}""";
        var parsed = engine.JsonParse(json);
        return engine.JsonStringify(parsed);
    }

    [Benchmark(Description = "Define a function, then invoke it 100 times")]
    public async Task<object> FunctionInvocation()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync(@"
export function add(a, b) {
    return a + b;
}
");
        object result = null!;
        for (var i = 0; i < 100; i++)
        {
            result = engine.InvokeFunction("add", i, i + 1);
        }
        return result;
    }

    [Benchmark(Description = "Execute async function with Promise.resolve")]
    public async Task<int?> AsyncAwait()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync(@"
async function fetchData() {
    const value = await Promise.resolve(42);
    return value;
}
export const result = await fetchData();
");
        return engine.GetValue<int>("result");
    }

    [Benchmark(Description = "Import Common module and call a function")]
    public async Task<string?> ModuleImport()
    {
        using var scope = _serviceProviderWithModules.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync(@"
import * as common from 'common'
export const guid = common.Guid.New().toString();
");
        return engine.GetValue<string>("guid");
    }

    [Benchmark(Description = "Execute fibonacci + array manipulation script")]
    public async Task<int?> LargeScript()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync(@"
function fibonacci(n) {
    if (n <= 1) return n;
    let a = 0, b = 1;
    for (let i = 2; i <= n; i++) {
        const temp = a + b;
        a = b;
        b = temp;
    }
    return b;
}

const fibs = [];
for (let i = 0; i < 20; i++) {
    fibs.push(fibonacci(i));
}

const sorted = [...fibs].sort((a, b) => b - a);
const filtered = sorted.filter(x => x > 10);
const mapped = filtered.map(x => x * 2);
export const result = mapped.reduce((acc, x) => acc + x, 0);
");
        return engine.GetValue<int>("result");
    }
}
