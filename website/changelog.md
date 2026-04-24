# Changelog

All notable changes to this project are documented in this file. For the authoritative source, see [`CHANGELOG.md`](https://github.com/cocoar-dev/Cocoar.JsEval/blob/main/CHANGELOG.md) in the repo root.

## [3.2.0]

### Added
- **`JsEngine.PrepareModule(string)`** — pre-parse an ES-module script (`import` / `export`) for repeated execution. Returns a thread-safe `JsPreparedModule` that can be cached globally and passed to `ExecuteAsync(JsPreparedModule)`.
- **`JsEngine.ExecuteAsync(JsPreparedModule)`** — overload that accepts a pre-parsed module.

### Changed (behavioural)
- **`ExecuteAsync` now follows standard ES-module semantics.** Top-level code runs once per unique script content on a given engine. Repeated `ExecuteAsync` calls on the same script return the cached module namespace instead of re-parsing and re-evaluating top-level statements. Matches how ES modules work in Node / browsers / Deno. **Migration:** scripts that relied on top-level code re-running (`export const id = Math.random()` yielding a different `id` per call) need per-call work exposed as exported functions (`export function newId() { return Math.random(); }`) invoked via `InvokeFunction`. For true per-call re-execution, use the lightweight path (`Evaluate(string)` / `EvaluateAsync(string)`) which has no module system.
- **Module cache eliminates the `__main_N__` memory leak** — long-lived engines no longer accumulate a new entry in Jint's module registry per `ExecuteAsync` call.

### Performance
- **Hot-loop `import` ~115× faster** on a pooled engine — `ExecuteAsync(string)` with a repeated script drops from 14 µs/call to 123 ns/call.
- **Pooled `ExecuteAsync(prepared)` ~9× faster** — 12 µs → 1.3 µs per call.
- See [`PERFORMANCE-COMPARISON.md`](https://github.com/cocoar-dev/Cocoar.JsEval/blob/main/PERFORMANCE-COMPARISON.md) for the full before/after table.

### Fixed (benchmark infrastructure)
- **Async/Task-interop benchmarks (`ValueBenchmarks.TaskInterop`, `EngineBenchmarks.AsyncAwait`) now run.** Both previously returned `NA` due to a scoped-DI disposal bug in the benchmark harness: the engine was disposed between iterations, hitting `ObjectDisposedException` on any async path. Benchmarks now open a fresh DI scope per iteration. Side effect: "Engine creation (cold start)" now measures real construction cost (~9.8 µs) instead of a DI cache lookup (~1.5 µs).

## [3.1.4]

### Added
- **`linq.d.ts` is auto-emitted when `AddLinq()` is on the builder.** Before this, Monaco flagged every `linq.guid('…')` / `linq.decimal('…')` with `Cannot find name 'linq'`. Now `TsDefinitionService.GetTsDefinitions()` surfaces the declaration automatically — via a new `IJsTsDefinitionContributor` extension point (any third-party package can plug in its own `.d.ts`).
- **`AddTypeAlias<T>("ShortName")` and `MapNamespace(prefix, target)` on the builder.** Aliases are emitted at **root scope** in the `.d.ts` so Monaco hovers show `CustomerView` instead of `TimeToDo.Infrastructure.Persistence.Marten.Projections.Customers.CustomerView` — *and* `NewObject("CustomerView")` resolves to the same type at runtime. One config, both layers. Cross-references between aliased/mapped types use the short name throughout so nested projections don't drag 60-char prefixes into every member.
- **`DefinitionBuilder.AddType(Type, string alias)` / `MapNamespace(source, target)`** for the standalone-builder path (non-DI consumers).

### Changed
- **`System.*` types are excluded from `MapNamespace` by default** — they stay fully qualified. `MapNamespace("", "")` does not re-home `System.Guid`; use explicit `AddTypeAlias` if you really want BCL renaming (unusual).
- **Collision detection fires only when a rule is involved.** Two distinct types that naturally share a short name (`Span<T>.Enumerator` / `ReadOnlySpan<T>.Enumerator`) keep the v3.1.3 behavior of emitting two matching `interface` declarations (TypeScript merges them). Once *any* alias or `MapNamespace` touches one side, the resolver throws at render-time with both source types named and actionable next steps — no silent overrides.

## [3.1.3]

### Removed (breaking for direct lib.* consumers)
- **`Cocoar.JsEval.TsDefinition` no longer ships `lib.es5.d.ts` or `lib.es2015.core.d.ts` via `GetTsDefinitions()`.** Those were vendored copies of a very old TypeScript standard library, pass-through resources for Monaco consumers — but Monaco loads its own version-matched libs internally, and stacking ours on top risked overriding fresher types. `global.d.ts` (hand-written, declares `fetch`, `NewObject`, `exit`, `require`) stays. Consumers who relied on the shipped libs can either use Monaco's built-in libs (default) or embed the current set from `Cocoar.JsEval.TypeScript.V8.EmbeddedResources.LibFiles`.

### Fixed
- **`Cocoar.JsEval.TsDefinition` — Rendered `.d.ts` output is now parse-clean TypeScript.** Three renderer bugs that produced output the TypeScript compiler refuses. Before the fix, 5 of 9 non-`lib.` files from the default module set failed to parse (`System`, `Cocoar`, `SqlKata`, `Dapper`, `AngleSharp` — 465 parse errors in `System.d.ts` alone); after the fix all 9 parse cleanly. The three issues:
  - `Task<T>` / `ValueTask<T>` emitted the generic argument twice (`Promise<T><T>`) — `NormalizeTypeName` baked it in and the caller appended it again. Fixed: `NormalizeTypeName` returns the bare wrapper, callers always own generic-arg emission.
  - `ref T` parameters / returns leaked the .NET ByRef suffix `&` into TS output (`Current: T&`), a parse error (intersection operator without right operand). The old `FullName.EndsWith('&')` check missed `ref T` on generic type parameters, where `FullName` is `null`. Now uses `IsByRef` / `IsPointer` with `GetElementType()`.
  - Name-colliding types were declared twice in the same namespace (40+ duplicates in `System.d.ts` — `Type`, `Attribute`, …). The renderer re-added the same cached `TypeDefinition` instance on every encounter. Now dedup'd on add.

### Changed
- **`TypeScriptRendererDefaults.NormalizeTypeName` contract (direct callers only).** For `Task<T>` / `ValueTask<T>` the method now returns `"Promise"` instead of `"Promise<T>"` — callers append generic args from `TypeDefinition.GenericArguments`. Non-generic `Task` / `ValueTask` still return `"Promise<void>"`. Consumers using the bundled `TypeScriptRenderer` see no behaviour change.

## [3.1.2]

### Fixed
- **`Cocoar.JsEval.TsDefinition` — `Guid` is no longer forced to `string`.** The type-mapping table had `[typeof(Guid)] = "string"`, erasing Guid-ness at the TypeScript layer. Now `Guid` falls through the normal rendering path. Consumers who want the old behaviour can re-add the mapping via `TypeScriptRendererDefaults.TypeMappings`.

### Added
- **`TsTranspiler.TranspileWithSourceMap(string)`** — returns JS + Source Map v3 JSON + non-error diagnostics. Lets you map runtime-error positions back to the original TypeScript source for Monaco-style editor integrations.

### Fixed
- **`TsTranspiler.Transpile(string)` now throws `TsTranspileException` on TypeScript syntax errors.** 3.1.1 and earlier silently returned a near-empty `"use strict";` string — failures surfaced later at `EvaluateExpression` time with generic parser errors. Syntax errors now carry a structured diagnostic list (category, code, message, line, column). Type errors (`const x: number = "oops"`) are still not reported — known limitation of `ts.transpileModule`; requires a future `createProgram`-based add-on.

## [3.1.1]

### Fixed
- **Optional chaining in LINQ provider predicates (Marten / EF Core / LINQ2DB).** v3.1.0 emitted nested `ConditionalExpression` nodes (one per `?.` guard), which Marten refused with `BadLinqExpressionException`. Verified end-to-end in all three sandbox projects (Marten + Postgres, EF Core + SQLite, LINQ2DB + SQLite) against the two real-world rollback cases from a downstream authorization project — natural v3.1.1, v3.1.0 `=== true` style, and hand-rolled workaround all return identical rows on every provider.

### Changed
- **Boolean-context optional chains flatten to pure `&&`-chains.** `where(p => p.Person?.Name.startsWith('A'))` produces `p.Person != null && p.Person.Name.StartsWith("A")` — zero `IIF` nodes, byte-identical to hand-written C#, translated natively by every mainstream LINQ provider.
- **Optional chains in binary comparisons flatten too.** `(t) => t.Customer?.Id === linq.guid(...)` emits `t.Customer != null && t.Customer.Id == guid` — no IIF-inside-comparison. Applies to `==` / `<` / `<=` / `>` / `>=` when the other operand is a known-non-null constant.
- **Redundant `x === true` / `x === false` collapse.** `u.IsActive === true` → `u.IsActive`, `u.IsActive === false` → `!u.IsActive` (plain `bool` operands only — `bool?` keeps lifted semantics). Unlocks v3.1.0-style `=== true` scripts against Marten.
- **`bool?` → `bool` coercion in boolean contexts (JS-truthy semantics).** Optional chaining inside `Where(...)`, negation (`!`), `&&` / `||`, and ternary test position no longer require an explicit `=== true`. `null` is treated as `false` at each boolean-context site. `where(p => !p.Person?.Name.startsWith('A'))` correctly returns rows where `Person` is null (JS: `!undefined === true`).

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
