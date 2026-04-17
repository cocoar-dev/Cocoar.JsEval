# Performance

Cocoar.JsEval is optimized for low-overhead script execution. This page documents benchmark results and guidance for choosing the right execution method.

::: info Benchmark Environment
All measurements were taken on an ARM-based development laptop (.NET 10, Jint 4.8, Release build, BenchmarkDotNet 0.14). Results on server hardware will differ — use these numbers for **relative comparison**, not as absolute targets.
:::

## Engine Benchmarks

Core engine operations, measured with a new engine instance per operation:

| Operation | Mean | Allocated |
|---|---:|---:|
| Engine Creation | 76 µs | 31 KB |
| Simple Expression (`2 + 3`) | 117 µs | 38 KB |
| SetValue + Execute + GetValue | 111 µs | 39 KB |
| JSON Parse + Stringify | 76 µs | 33 KB |
| Function Invocation (100x) | 284 µs | 120 KB |
| Async/Await (Promise.resolve) | 179 µs | 62 KB |
| Module Import + Call | 238 µs | 77 KB |
| Fibonacci(30) + Array Sort | 1,096 µs | 312 KB |

## Execution Methods Compared

The choice of execution method has a significant impact on performance. The table below shows the same script (`query.WhereResponsible(ctx.UserId)`) executed via different methods:

### New Engine per Call

| Method | Simple Script | Complex Script | Allocated |
|---|---:|---:|---:|
| `Evaluate(string)` | 60 µs | 99 µs | 65 KB |
| `Evaluate(prepared)` | 55 µs | 79 µs | 57 KB |
| `ExecuteAsync(string)` | 63 µs | ~100 µs | 40 KB |

The overhead is dominated by engine creation (~55 µs), not the script itself. The difference between `Evaluate` and `ExecuteAsync` is minimal when creating a new engine per call.

### Reused Engine

| Method | Simple Script | Medium Script | Complex Script | Allocated |
|---|---:|---:|---:|---:|
| `Evaluate(prepared)` | **0.8 µs** | **3.2 µs** | **4.2 µs** | 9.7 KB |

When reusing an engine instance, the creation overhead is eliminated. Combined with a pre-parsed script, this is the fastest execution path.

### One-Time Costs

| Operation | Cost | When |
|---|---:|---|
| `JsEngine.Prepare(script)` | 7 µs | Once per script (cacheable, thread-safe) |
| TypeScript Transpile | 1-2 s | Once per script change (e.g., on save in admin UI) |

## JS → LINQ Translator

The [`Cocoar.JsEval.Linq`](/guide/linq) translator turns a JS arrow function into a real `Expression<Func<T, TResult>>`. Measurements assume a reused Jint engine with the JS function already parsed (the typical hot-loop case: translate the same predicate repeatedly when re-running a query).

| Predicate shape | Mean | Allocated |
|---|---:|---:|
| Simple boolean property (`u => u.IsActive`) | **0.24 µs** | 632 B |
| String method (`u => u.Name.startsWith('A')`) | 0.53 µs | 1,232 B |
| Complex 3-clause `&&` | 0.78 µs | 1,688 B |
| `CsDateTime.AddDays` + implicit op | 0.78 µs | 1,616 B |
| Nested lambda (`u => u.Tags.some(t => …)`) | 0.95 µs | 1,680 B |
| Cold (re-parse + translate) | 2.58 µs | 5,160 B |

**Takeaway:** All shapes stay under ~1 µs warm. The translator sits at the same order of magnitude as a reused-engine `Evaluate(prepared)` call — effectively free versus the surrounding request cost.

### Hot-loop use case

For an ABAC-style rule engine that evaluates the same predicate thousands of times per request (e.g. 10 000 objects through a dynamic filter), the translator contributes only **~9.5 ms at 10 000 iterations even for the most expensive shape (nested lambda)**. A typical database round-trip (5–50 ms) dwarfs that.

### Reflection cache

An internal `ReflectionCache` (`ConcurrentDictionary`-backed, keyed by type + name + arg signature) memoizes every `GetProperty` / `GetMethod` / `GetImplicitCastMethodTo` / `MakeGenericMethod` call. The cache is warmed on first use and has no eviction — reflection info is immutable. The impact is most visible on nested lambdas and wrappers with implicit operators:

| Shape | Before cache | After cache | Δ |
|---|---:|---:|---:|
| Nested lambda | 2.74 µs / 6.6 KB | 0.95 µs / 1.7 KB | **−65% / −74%** |
| CsDateTime + implicit op | 1.31 µs / 3.0 KB | 0.78 µs / 1.6 KB | **−40% / −46%** |

Simple predicates have nothing to cache and stay roughly the same (~15 ns dictionary-lookup overhead swallowed by noise).

## Choosing the Right Method

```
Do you know what the script contains?
├── No → ExecuteAsync (standard, always safe)
└── Yes
    ├── Needs import/export or modules? → ExecuteAsync
    └── No modules needed
        ├── Needs async/await? → EvaluateAsync
        └── Synchronous
            ├── Called once? → Evaluate(string)
            └── Called repeatedly? → Prepare + Evaluate(prepared)
```

## Relative Overhead

For context, here's how JsEval compares to other parts of a typical API request:

```
Typical GET /api/resource request:

Database Query:                   5,000 - 50,000 µs  (5-50 ms)
HTTP/Middleware Pipeline:           500 -  2,000 µs
JSON Serialization:                 100 -    500 µs
Auth/Token Validation:               50 -    200 µs
─────────────────────────────────────────────────────
JsEval (new engine):                 55 -     99 µs  (0.1 - 2.0%)
JsEval (reused engine + prepared): 0.8 -    4.2 µs  (< 0.1%)
```

In both cases, JsEval is not the bottleneck. The database query is 500x to 60,000x more expensive than script evaluation.

## Optimization Techniques

### Pre-Parse Scripts

If a script is executed repeatedly (e.g., a policy that runs on every request), parse it once and reuse:

```csharp
// At startup or when the script changes
var prepared = JsEngine.Prepare(compiledScript);

// Per request
engine.Evaluate(prepared);  // no parsing overhead
```

### Reuse Engine Instances

Engine creation is the largest fixed cost (~55 µs). If your scripts don't use the module system and you control the globals, you can reuse engine instances:

```csharp
// Create once
var engine = sp.GetRequiredService<IJsEngine>();

// Reuse — SetValue overwrites previous values
engine.SetValue("input", newData);
engine.Evaluate(prepared);
```

::: warning
A reused engine retains state between calls. Variables set in one script persist to the next. This is safe when you always overwrite globals via `SetValue` before each execution, but be aware that internal script state (variables defined inside the script) also persists.
:::

### Merge Multiple Scripts

If you need to evaluate multiple scripts on the same engine, merge them into one to avoid per-script overhead:

```csharp
var merged = string.Join("\n", scripts);
var prepared = JsEngine.Prepare(merged);
engine.Evaluate(prepared);  // one execution instead of N
```

## Running Benchmarks

The benchmark suite is included in the repository:

```bash
cd src/Benchmarks/JsEval.Benchmarks

# Run all benchmarks
dotnet run -c Release -- --filter "*"

# Run specific benchmark class
dotnet run -c Release -- --filter "*EngineBenchmarks*"
dotnet run -c Release -- --filter "*LightweightEvalBenchmarks*"
dotnet run -c Release -- --filter "*TranspilerBenchmarks*"
dotnet run -c Release -- --filter "*LinqTranslatorBenchmarks*"
```
