using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.TypeScript;
using Microsoft.Extensions.DependencyInjection;

namespace JsEval.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class TranspilerBenchmarks
{
    private ServiceProvider _serviceProvider = null!;
    private TsTranspiler _transpiler = null!;
    private JsEngine? _engine;

    private const string SimpleTs = @"
const name: string = 'hello';
const count: number = 42;
export const result: string = `${name}-${count}`;
";

    private const string InterfaceTs = @"
interface User {
    name: string;
    age: number;
    active: boolean;
    email?: string;
}

interface Address {
    street: string;
    city: string;
    zip: string;
}

const user: User = { name: 'Alice', age: 30, active: true };
const addr: Address = { street: '123 Main St', city: 'Springfield', zip: '62701' };
export const userName: string = user.name;
export const city: string = addr.city;
";

    private const string EnumTs = @"
enum Direction { Up, Down, Left, Right }
enum Color { Red = 'RED', Green = 'GREEN', Blue = 'BLUE' }
export const dir: number = Direction.Down;
export const color: string = Color.Green;
";

    private const string ClassTs = @"
class Calculator {
    private value: number;
    constructor(initial: number) { this.value = initial; }
    add(n: number): Calculator { this.value += n; return this; }
    subtract(n: number): Calculator { this.value -= n; return this; }
    multiply(n: number): Calculator { this.value *= n; return this; }
    getResult(): number { return this.value; }
}
const calc = new Calculator(10); //ignore
export const result: number = calc.add(5).subtract(2).multiply(3).getResult();
";

    [GlobalSetup]
    public void GlobalSetup()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        sc.AddTsTranspiler();
        _serviceProvider = sc.BuildServiceProvider();

        _transpiler = _serviceProvider.GetRequiredService<TsTranspiler>();

        // Warm up the transpiler engine pool with one transpilation
        _transpiler.Transpile("const x: number = 1;");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _serviceProvider.Dispose();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _engine?.Dispose();
        _engine = null;
    }

    [Benchmark(Description = "Transpile a simple typed expression")]
    public string TranspileSimple()
    {
        return _transpiler.Transpile(SimpleTs);
    }

    [Benchmark(Description = "Transpile code with interfaces and type annotations")]
    public string TranspileInterface()
    {
        return _transpiler.Transpile(InterfaceTs);
    }

    [Benchmark(Description = "Transpile code with enums")]
    public string TranspileEnum()
    {
        return _transpiler.Transpile(EnumTs);
    }

    [Benchmark(Description = "Transpile a class with typed members")]
    public string TranspileClass()
    {
        return _transpiler.Transpile(ClassTs);
    }

    [Benchmark(Description = "Full pipeline: transpile TS then execute JS")]
    public async Task<int?> TranspileAndExecute()
    {
        _engine = _serviceProvider.GetRequiredService<JsEngine>();
        var js = _transpiler.Transpile(ClassTs);
        await _engine.ExecuteAsync(js);
        return _engine.GetValue<int>("result");
    }

    [Benchmark(Description = "First transpilation (cold start, pool empty)")]
    public string ColdStart()
    {
        // Create a fresh transpiler to simulate cold start
        var freshTranspiler = new TsTranspiler();
        return freshTranspiler.Transpile(SimpleTs);
    }

    [Benchmark(Description = "Second+ transpilation (warm start, pool has engine)")]
    public string WarmStart()
    {
        // The shared transpiler already has engines in the pool
        return _transpiler.Transpile(SimpleTs);
    }
}
