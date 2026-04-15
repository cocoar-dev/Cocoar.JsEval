using System;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace JsEval.Benchmarks;

/// <summary>
/// Native C# baseline — the same operations done directly in C#,
/// so we can measure the exact overhead of the JS engine layer.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class NativeBaselineBenchmarks
{
    // --- Equivalent to "export const x = 2 + 3" + GetValue<int> ---

    [Benchmark(Description = "[C#] Simple arithmetic (2 + 3)")]
    public int SimpleArithmetic()
    {
        var x = 2 + 3;
        return x;
    }

    // --- Equivalent to SetValue + Execute + GetValue ---

    private string _input = "Hello";

    [Benchmark(Description = "[C#] String concat (SetValue + compute + GetValue)")]
    public string StringConcat()
    {
        var input = _input;
        var output = input + " World";
        return output;
    }

    // --- Equivalent to JsonParse + JsonStringify ---

    private readonly string _json = """{"name":"Test","value":42}""";

    [Benchmark(Description = "[C#] JSON parse + stringify cycle")]
    public string JsonRoundTrip()
    {
        var parsed = JsonSerializer.Deserialize<JsonElement>(_json);
        return JsonSerializer.Serialize(parsed);
    }

    // --- Equivalent to function invocation (100x) ---

    [Benchmark(Description = "[C#] Function call 100x (add)")]
    public double FunctionInvocation100x()
    {
        double result = 0;
        for (int i = 0; i < 100; i++)
            result = Add(result, i);
        return result;
    }

    private static double Add(double a, double b) => a + b;

    // --- Equivalent to async/await ---

    [Benchmark(Description = "[C#] Async/await (Task.FromResult)")]
    public async Task<int> AsyncAwait()
    {
        var result = await Task.FromResult(42);
        return result;
    }

    // --- Equivalent to fibonacci + array script ---

    [Benchmark(Description = "[C#] Fibonacci(30) + array sort")]
    public int LargeComputation()
    {
        var fib = Fibonacci(30);

        var arr = new int[100];
        for (int i = 0; i < 100; i++)
            arr[i] = 100 - i;
        Array.Sort(arr);

        var sum = 0;
        for (int i = 0; i < arr.Length; i++)
            sum += arr[i];

        return fib + sum;
    }

    private static int Fibonacci(int n)
    {
        if (n <= 1) return n;
        return Fibonacci(n - 1) + Fibonacci(n - 2);
    }
}
