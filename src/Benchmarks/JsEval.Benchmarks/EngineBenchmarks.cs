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
    private JsEngine? _engine;

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

    [IterationCleanup]
    public void IterationCleanup()
    {
        _engine?.Dispose();
        _engine = null;
    }

    [Benchmark(Description = "Create a new JsEngine (cold-start cost)")]
    public JsEngine EngineCreation()
    {
        _engine = _serviceProvider.GetRequiredService<JsEngine>();
        return _engine;
    }

    [Benchmark(Description = "Execute 'export const x = 2 + 3' and get value")]
    public async Task<int?> SimpleExpression()
    {
        _engine = _serviceProvider.GetRequiredService<JsEngine>();
        await _engine.ExecuteAsync("export const x = 2 + 3;");
        return _engine.GetValue<int>("x");
    }

    [Benchmark(Description = "SetValue + Execute + GetValue round-trip")]
    public async Task<string?> SetAndGetValue()
    {
        _engine = _serviceProvider.GetRequiredService<JsEngine>();
        _engine.SetValue("input", "Hello");
        await _engine.ExecuteAsync("export const output = input + ' World';");
        return _engine.GetValue<string>("output");
    }

    [Benchmark(Description = "JsonParse then JsonStringify cycle")]
    public string JsonParseStringify()
    {
        _engine = _serviceProvider.GetRequiredService<JsEngine>();
        var json = """{"name":"Test","value":42,"nested":{"a":1,"b":2}}""";
        var parsed = _engine.JsonParse(json);
        return _engine.JsonStringify(parsed);
    }

    [Benchmark(Description = "Define a function, then invoke it 100 times")]
    public async Task<object> FunctionInvocation()
    {
        _engine = _serviceProvider.GetRequiredService<JsEngine>();
        await _engine.ExecuteAsync(@"
export function add(a, b) {
    return a + b;
}
");
        object result = null!;
        for (var i = 0; i < 100; i++)
        {
            result = _engine.InvokeFunction("add", i, i + 1);
        }
        return result;
    }

    [Benchmark(Description = "Execute async function with Promise.resolve")]
    public async Task<int?> AsyncAwait()
    {
        _engine = _serviceProvider.GetRequiredService<JsEngine>();
        await _engine.ExecuteAsync(@"
async function fetchData() {
    const value = await Promise.resolve(42);
    return value;
}
export const result = await fetchData();
");
        return _engine.GetValue<int>("result");
    }

    [Benchmark(Description = "Import Common module and call a function")]
    public async Task<string?> ModuleImport()
    {
        _engine = _serviceProviderWithModules.GetRequiredService<JsEngine>();
        await _engine.ExecuteAsync(@"
import * as common from 'common'
export const guid = common.Guid.New().toString();
");
        return _engine.GetValue<string>("guid");
    }

    [Benchmark(Description = "Execute fibonacci + array manipulation script")]
    public async Task<int?> LargeScript()
    {
        _engine = _serviceProvider.GetRequiredService<JsEngine>();
        await _engine.ExecuteAsync(@"
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
        return _engine.GetValue<int>("result");
    }
}
