# Performance

Cocoar.JsEval is optimized for low-overhead script execution. This page documents
benchmark results and guidance for choosing the right execution method.

::: info Benchmark Environment
All measurements on Windows 11 ARM64, .NET 10.0.6, Jint 4.8, Release build,
BenchmarkDotNet 0.14. Numbers are arithmetic means after warm-up. Use the numbers
for **relative comparison** — absolute timings on x64 server hardware will differ.

These figures were measured on Jint 4.8 and have **not** been re-run against the
4.15.3 upgrade in 5.0.0. Treat the relative ordering of the execution methods as
current and the absolute numbers as indicative.
:::

## Engine Benchmarks

Core engine operations, measured with a new engine instance per operation:

| Operation                              | Mean     | Allocated |
| -------------------------------------- | -------: | --------: |
| Engine Creation                        |   9.8 µs |     38 KB |
| Simple Expression (`2 + 3`)            |    13 µs |     47 KB |
| SetValue + Execute + GetValue          |    14 µs |     47 KB |
| JSON Parse + Stringify                 |    11 µs |     41 KB |
| Function Invocation (100×)             |    43 µs |    121 KB |
| Async/Await (`Promise.resolve`)        |    22 µs |     70 KB |
| Module Import + Call                   |    37 µs |     61 KB |
| Fibonacci + Array Sort                 |   186 µs |    276 KB |

## Execution Methods Compared

The choice of execution method has a significant impact on performance. The table below
shows the same script (`query.WhereResponsible(ctx.UserId)`) executed via different methods.

### New Engine per Call

| Method                         | Simple Script | Complex Script | Allocated (simple) |
| ------------------------------ | ------------: | -------------: | -----------------: |
| `Evaluate(string)`             |       1.8 µs  |         30 µs  |            45 KB   |
| `Evaluate(prepared)`           |       1.4 µs  |         25 µs  |            43 KB   |
| `ExecuteAsync(string)`         |      16 µs    |              — |            49 KB   |
| `ExecuteAsync(prepared)`       |     ~14 µs    |              — |           ~47 KB   |

The overhead is dominated by engine creation (~10 µs), not the script itself. The
module path (`ExecuteAsync`) is ~10× the lightweight `Evaluate` path because it
goes through the full ES-module resolver + linker. `ExecuteAsync(prepared)` saves
the parse cost (~6 µs) on the first call — the gain is modest on a fresh engine
but becomes ~9× on a **pooled** engine (see table below).

### Pooled Engine (reused instance)

| Method                                   | Simple Script | Medium Script | Complex Script | Allocated |
| ---------------------------------------- | ------------: | ------------: | -------------: | --------: |
| `Evaluate(prepared)`                     |   **0.75 µs** |      3.15 µs  |     **4.0 µs** |   9.7 KB  |
| `ExecuteAsync(string)` — cached module   |   **0.12 µs** |             — |              — |     304 B |
| `ExecuteAsync(prepared)` — cached module |    **1.3 µs** |             — |              — |   5.3 KB  |

The first call on a pooled engine pays the full cost (parse + link + execute).
Subsequent calls on the **same script content** hit the module cache — the
top-level code ran once, the module namespace is returned directly.
Scripts should export functions and be invoked via `InvokeFunction` for per-call work.

### One-Time Costs

| Operation                          | Cost     | When                                                 |
| ---------------------------------- | -------: | ---------------------------------------------------- |
| `JsEngine.Prepare(script)`         |   6.3 µs | Once per script (cacheable, thread-safe)             |
| `JsEngine.PrepareModule(script)`   | ~similar | Once per module (cacheable, thread-safe)             |
| TypeScript Transpile (`.ts` → JS)  |   30-90 ms | Once per script change (e.g., on save in admin UI)   |

## Module Semantics (Model B)

`ExecuteAsync(string)` and `ExecuteAsync(JsPreparedModule)` follow standard ES-module
semantics: **top-level code runs once per unique module content on a given engine**.
Repeated executions of the same script return the cached module namespace without
re-parsing or re-evaluating top-level statements.

```js
// ⚠ Anti-pattern — top-level code with per-call side effects
export const id = Math.random();  // Same value on every ExecuteAsync call

// ✅ Correct — per-call work in exported functions
export function newId() { return Math.random(); }
// Call via: engine.InvokeFunction("newId")
```

This matches how ES modules work everywhere else (Node, browsers, Deno). For legacy
"re-run everything on every call" semantics, use the lightweight path:
`Evaluate(string)` or `Evaluate(prepared)` — which have no module system and
always execute the full script.

## JS → LINQ Translator

The [`Cocoar.JsEval.Linq`](/guide/linq) translator turns a JS arrow function into a real
`Expression<Func<T, TResult>>`. Measurements assume a reused Jint engine with the JS
function already parsed (the typical hot-loop case: translate the same predicate
repeatedly when re-running a query).

| Predicate shape                              | Mean     | Allocated |
| -------------------------------------------- | -------: | --------: |
| Simple boolean property (`u => u.IsActive`)  | **0.24 µs** |   632 B |
| String method (`u => u.Name.startsWith('A')`)|    0.49 µs |  1,232 B |
| Complex 3-clause `&&`                        |    0.78 µs |  1,688 B |
| `CsDateTime.AddDays` + implicit op           |    0.80 µs |  1,616 B |
| Nested lambda (`u => u.Tags.some(t => …)`)   |    0.95 µs |  1,680 B |
| Cold (re-parse + translate)                  |    2.49 µs |  5,160 B |

**Takeaway:** All shapes stay under ~1 µs warm. The translator sits at the same order
of magnitude as a reused-engine `Evaluate(prepared)` call — effectively free versus
the surrounding request cost.

### Hot-loop use case

For an ABAC-style rule engine that evaluates the same predicate thousands of times
per request (e.g. 10 000 objects through a dynamic filter), the translator contributes
only **~9.5 ms at 10 000 iterations even for the most expensive shape (nested lambda)**.
A typical database round-trip (5–50 ms) dwarfs that.

### Reflection cache

An internal `ReflectionCache` (`ConcurrentDictionary`-backed, keyed by type + name + arg signature)
memoizes every `GetProperty` / `GetMethod` / `GetImplicitCastMethodTo` / `MakeGenericMethod` call.
The cache is warmed on first use and has no eviction — reflection info is immutable.

## Choosing the Right Method

```
Do you know what the script contains?
├── No → ExecuteAsync (standard, always safe, cached on repeat)
└── Yes
    ├── Needs import/export or modules? → ExecuteAsync (cached on repeat)
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
JsEval (new engine):                 14 -     37 µs  (0.03 - 0.7%)
JsEval (reused engine + prepared): 0.12 -    4.0 µs  (< 0.1%)
```

In both cases, JsEval is not the bottleneck. The database query is 100× to 60,000×
more expensive than script evaluation.

## Optimization Techniques

### Pre-Parse Scripts

If a script is executed repeatedly (e.g., a policy that runs on every request),
parse it once and reuse:

```csharp
// At startup or when the script changes
var prepared = JsEngine.Prepare(compiledScript);

// Per request
engine.Evaluate(prepared);  // no parsing overhead
```

For modules (with `import`), use `PrepareModule`:

```csharp
var preparedModule = JsEngine.PrepareModule(moduleScript);

// Per request
await engine.ExecuteAsync(preparedModule);
engine.InvokeFunction("rule", ctx);
```

### Reuse Engine Instances

Engine creation is the largest fixed cost (~10 µs). With the scoped DI lifetime
(since v3.1), injecting `JsEngine` in the same DI scope gives you a shared instance —
per-HTTP-request pooling comes for free:

```csharp
// Scoped DI: multiple services in the same request share this engine
public class RuleEvaluator(JsEngine engine) { ... }
```

::: warning
A reused engine retains state between calls. Variables set in one script persist
to the next. Internal script state (top-level `const`, `let`) also persists across
calls — which is why the module cache returns the cached namespace rather than
re-running top-level code.
:::

### Merge Multiple Scripts

If you need to evaluate multiple non-module scripts on the same engine, merge them
into one to avoid per-script overhead:

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
