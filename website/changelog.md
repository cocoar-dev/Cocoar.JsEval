# Changelog

All notable changes to this project are documented in this file. For the authoritative source, see [`CHANGELOG.md`](https://github.com/cocoar-dev/Cocoar.JsEval/blob/main/CHANGELOG.md) in the repo root.

## [3.1.0]

### Added
- **`Cocoar.JsEval.Linq` — Optional chaining (`?.`) in predicates.** `(t) => t.Customer?.Label.startsWith('A')` now translates to a null-safe Expression tree. Each `?.` step short-circuits the rest of the chain to `null` if the guarded target is null. Skips the guard when the target is a non-nullable value type. Works for both LINQ-provider translation and in-memory `Expression.Compile()`.
- **`Cocoar.JsEval.Linq` — Nullish coalescing (`??`) in predicates.** `(u) => u.Name ?? 'anon'` translates to `Expression.Coalesce(…)`. Combines naturally with optional chaining: `(u) => (u.Address?.City ?? '') === 'Vienna'`.

### Changed
- **`AddJsEval` now registers `JsEngine` as scoped** instead of transient. Multiple services resolved in the same DI scope now share one engine — globals set via `SetValue` are visible across collaborators, and the "Jint is not thread-safe" contract is enforced at the DI level. Consumers needing isolated engines can still instantiate via `new JsEngine(...)`.

## [3.0.0]

v3.0.0 rolls up v2.0.0 plus a small but breaking API cleanup surfaced by first-adopter integration. v2.0.0 was unlisted from NuGet the same day; install v3.0.0 directly.

### Breaking (v2.0.0 → v3.0.0)
- **`IJsEngine` interface removed.** The interface claimed an engine-swap abstraction that the library does not actually support (the whole codebase depends on Jint's `JsValue` / `ScriptFunction` / Acornima AST). `JsEngine` is now the public contract.
  - **Migration:** replace `sp.GetRequiredService<IJsEngine>()` with `sp.GetRequiredService<JsEngine>()`; replace `IJsEngine` parameter/field types with `JsEngine`. No other behaviour changed.

### Added in v3.0.0
- **`JsEngine.UnderlyingEngine { get; }`** — direct public access to the underlying `Jint.Engine`.
- **`JsEngine.EvaluateExpression(string): JsValue`** — expression-semantics evaluation returning the resulting `JsValue` (the existing `Evaluate(string)` remains statement-semantics / `void`). The common "evaluate a JS expression, hand it to the Linq translator" flow no longer needs a `__result` global or reflection hack.
- **`Cocoar.JsEval.Linq` convenience overloads** — `JsExpressionTranslator.Translate` / `TranslateLambda` / `JsLinqContext.Scope` accept `JsEngine` directly (in addition to the `Jint.Engine` overloads).

### Breaking (v1.0.0 → v2.0.0, carried into v3.0.0)
- **`Cocoar.JsEval.Expressions` package removed.** Its string-DSL expression builder is superseded by `Cocoar.JsEval.Linq`, which produces the same Expression Trees from JS/TS predicates written in natural syntax.
  - **Migration:** rewrite `ExpressionHelper.Equal<T, V>("Name", value)` as JS — `users.where(u => u.Name === value)`. For dotted paths, `ExpressionHelper.StartsWith<T>("Customer.Name", "A")` becomes `users.where(u => u.Customer.Name.startsWith('A'))`.

### Added in v2.0.0 (carried into v3.0.0)
- **Cocoar.JsEval.Linq** — JS arrow functions → real .NET Expression Trees
  - `JsExpressionTranslator`, `JsLinqExtensions` (natural LINQ names `Where` / `Find` / `Count` / `Any` / `OrderBy` / `OrderByDescending` / `ThenBy` / `ThenByDescending`), `JsLinqContext`, `IJsMethodMap`
  - `ExpressionDependencyCollector` + `PropertyDependencies` for reactive re-execution / cache invalidation
  - Byte-identical SQL to hand-written C# source lambdas, verified against Marten / EF Core / LINQ2DB
  - `linq.*` typed literal DSL (`decimal` / `double` / `int` / `long` / `date` / `dateUtc` / `dateOffset` / `dateOnly` / `timeOnly` / `timeSpan` / `guid`) and zero-arg time helpers (`today` / `now` / `utcNow` / `todayUtc`)
  - C# LINQ aliases accepted alongside JS names (`arr.Any`/`All`/`Where`/`Select`/`FirstOrDefault`/`Contains`, `str.Contains`/`StartsWith`/`EndsWith`/`ToLower`/`ToUpper`/`Trim`)
  - `LinqTypeScriptDefinition` — embedded `.d.ts` with TS declaration merging for Monaco/VS Code IntelliSense
  - Enum auto-coercion (string or numeric literal to typed enum constant, ORM-neutral)
  - User-defined `implicit operator` conversions for wrappers like `CsDateTime`
  - Constant-folding on unary numeric negation (Marten compatibility)
  - Internal `ReflectionCache` (~65% faster, ~74% fewer allocations on nested lambdas)
- **Cocoar.JsEval.Engine** additions:
  - `CsDateTime` with fluent `AddDays`/`AddMonths`/… API, implicit operators to/from `DateTime`, static factories
  - `JsEvalBuilder.EnableCsDateTime()` — opt-in registration as JS global
  - `JsEvalBuilder.RegisterEngineConfigurator(Action<Engine>)` — extension point for add-on packages

## [1.0.0]

### Added
- JavaScript execution via Jint 4.8 (ES2025 support)
- Four execution methods: `ExecuteAsync`, `Evaluate`, `Evaluate(prepared)`, `EvaluateAsync`
- Pre-parsed scripts (`Prepare`) for maximum throughput
- TypeScript 6.0 transpilation with embedded compiler and engine pooling
- `fetch()` API with opt-in sandboxing (WHATWG-compliant lowercase properties)
- Automatic .NET `Task` → JS `Promise` interop
- Built-in globals: `console`, `setTimeout`/`setInterval`, `structuredClone`, `atob`/`btoa`, `performance.now()`, `TextEncoder`/`TextDecoder`
- 8 extensible modules (Common, HTTP, Database, SMTP, Template, AngleSharp, Logging, VFS)
- `.d.ts` generation for IntelliSense (`TsDefinitionService`)
- Fluent builder pattern: `AddJsEval(b => b.EnableFetch().AddModule<T>())`
- Engine reuse: both `ExecuteAsync` and `Evaluate` support multiple calls on the same engine
- VitePress documentation website
