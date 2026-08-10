# Changelog

All notable changes to this project are documented in this file. For the authoritative source, see [`CHANGELOG.md`](https://github.com/cocoar-dev/Cocoar.JsEval/blob/main/CHANGELOG.md) in the repo root.

## [5.0.0] — Untrusted-script hardening: interop restrictions, Jint 4.15.3

**Breaking.** Passing an object to a script grants more than the object — it grants everything reachable from it. [`AllowOnly`](/guide/engine#restricting-what-a-script-can-reach) and `DenyTypes` make that surface a decision instead of a consequence, and [`Sandboxed()`](/guide/engine#sandboxed-mode) locks the runtime down. All three are opt-in. The upgrade to Jint 4.15.3 changes how a script sees a CLR array, which is the only change that can affect existing scripts.

### Added
- **`Sandboxed()`** — strict mode, no `eval`/`Function`, invariant culture/UTC, memory/recursion/stack/array/regex limits, no `GetType()` or reflection, no shared-memory primitives, frozen prototypes. Latches in **both** directions: `EnableFetch()` before or after it throws. CLR interop stays on — it hardens the runtime, it does **not** narrow the object graph.
- **`AllowOnly(a => a.Member(...).Method(...).Type<T>())`** — declares the members a script may reach; everything else stops existing for it, through `Object.keys`, `for..in` and `JSON.stringify` alike. Members are named through expressions, so a rename is a compile error rather than a silently narrower sandbox.
- **`DenyTypes(params Type[])`** — refuses types outright, checked on the declared *and* the runtime type, so a member declared as `object` cannot smuggle one through.
- **`ConfigureJint(Action<Jint.Options>)`** — reaches Jint options that only apply at construction time.
- **`WithMaxJsonDepth(int)`** on `JsEngineOptions` (default 512).
- **`TranslationOptions.IdentifierResolver`** (Cocoar.JsEval.Linq) — resolves a free identifier in a rule to a host object, which is how a rule reaches an imported module. Constant calls on a resolved object are folded during translation.

### Changed
- **Jint 4.8.0 → 4.15.3.** A CLR `T[]` is now a live view rather than a copy. Index writes, `sort` and `reverse` reach the underlying array instead of being **silently discarded**; `push` and `length =` throw, because a fixed-size array cannot honour them. `Array.isArray(hostArray)` is now `false`, and `host.Tags === host.Tags` is now `true`. `List<T>` keeps full mutability and is unaffected, as are `map`, `filter`, `join`, `slice`, spread, `for..of`, `Object.keys` and `JSON.stringify`.
- **Generated `.d.ts`** declares an outbound CLR array as `ClrArray<T>` so TypeScript rejects `push` instead of allowing it. Inbound parameters stay `T[]`.
- **A module exception keeps its own message** instead of surfacing as `TargetInvocationException`'s "Exception has been thrown by the target of an invocation".

### Security
- **`Scriban` 7.1.0 → 7.2.6** (`Cocoar.JsEval.Module.Template`) and **`AngleSharp` 1.4.0 → 1.7.1** (`Cocoar.JsEval.Module.AngleSharp`) — both shipped versions carried published advisories (Scriban 2× high / 2× moderate, AngleSharp 1× moderate). Consumers of those two module packages get the updated dependency transitively.
- Microsoft.Extensions.* and the SQLite/EF packages moved to 10.0.10, `Microsoft.SourceLink.GitHub` to 10.0.301. `Marten` 8.33.0 → 9.11.0 and a `SQLitePCLRaw` pin affect only the test and experiment projects, which are not packaged. The solution now builds with no vulnerability warnings.


### Known limitation

- **`count`, `find` and `any` require a provider that permits synchronous execution.** They are terminal and execute the query where they stand, because a JavaScript expression has to return a value. **Marten 9 permits asynchronous data access only** and throws `NotSupportedException` on all three; `where`, `orderBy` and `thenBy` are lazy and unaffected, as are EF Core, LINQ2DB, in-memory queryables and Marten 8. JsEval ships no async counterparts on purpose — that would tie a provider-neutral library to one provider. The [LINQ guide](/guide/linq) shows the ten lines a host adds for its own provider, and the build-in-JS, terminate-in-C# alternative.
### Fixed
- **A script could terminate the host process.** Jint's JSON serializer recurses per level, so a ~120-byte script nesting a few thousand objects exhausted the .NET stack and killed the process with an uncatchable `StackOverflowException` — while staying inside every configured limit. `GetValue<T>` and `JsonStringify` now throw `InvalidOperationException` instead.
- **A module could not accept a `JsValue` parameter.** Every argument went through `ToObject()`, which turns a JS arrow function into a delegate the parameter then rejected — so a module could not receive a rule to translate.

### Migration

Existing code keeps working unchanged. Review scripts only if they *write to* a CLR `T[]`:

```js
// Silently lost before, now takes effect on the host's array:
host.Tags.sort();
// Worked before (and lost the write), now throws:
host.Tags.push('x');
// Was true before, now false:
Array.isArray(host.Tags);
```

## [4.1.0] — Constructor-pure JsEngine (Wolverine 6 / static-analysis friendly)

`JsEngine` no longer takes `IServiceProvider` directly, and `AddJsEval` now registers both `JsEngine` and `IJsModuleBuilder` **type-based** rather than via opaque lambda factories. Apps on Wolverine 6's strict `ServiceLocationPolicy.NotAllowed` default can inject `JsEngine` into handlers without per-consumer `AlwaysUseServiceLocationFor<T>` allowlist entries — the single remaining entry needed is `IJsModuleBuilder`. Consumers using `services.AddJsEval(...)` are unaffected.

### Added
- **`IJsModuleBuilder` / `JsModuleBuilder`** in `Cocoar.JsEval` — owns the `IServiceProvider`-based module activation (constructor-parameter resolution via DI + `ActivatorUtilities`). The single intentional locator boundary in the package; registered scoped by `AddJsEval`.

### Changed
- **`JsEngine` constructor** — now `JsEngine(IJsModuleRegistry, IJsModuleBuilder, JsEngineOptions, ILogger<JsEngine>?)`. No more `IServiceProvider`. Only direct `new JsEngine(...)` callers need to update.
- **`IJsModuleRegistry`** reduced to `GetRegisteredModuleDefinitions()`. The `BuildModuleInstance` / `BuildSingleModuleInstance` methods moved to `IJsModuleBuilder`.
- **DI registration in `AddJsEval`** — switched from lambda-factory closures to type-based registration (`AddScoped<JsEngine>()`, `TryAddScoped<IJsModuleBuilder, JsModuleBuilder>()`). Wolverine 6's strict codegen rejects opaque `ImplementationFactory` closures regardless of how clean the underlying ctor is.

### Migration (only for direct `new JsEngine(...)` callers)

```csharp
// Before (4.0):
new JsEngine(serviceProvider, moduleRegistry, options, logger);

// After (4.1):
new JsEngine(moduleRegistry, new JsModuleBuilder(serviceProvider, moduleRegistry), options, logger);
```

## [4.0.0] — Security hardening: minimal-mode defaults

**Breaking.** Unsafe-by-default JS globals are now off by default. See [SECURITY.md](https://github.com/cocoar-dev/Cocoar.JsEval/blob/main/SECURITY.md) for the threat model.

### Added
- **Opt-in builder flags** — `EnableNewObject()`, `EnableRequire()`, `EnableTimers()`, `EnableConsole()`. Symmetrical with the existing `EnableFetch()` / `EnableDebugMode()`.
- **`EnableNewObjectAssemblyFallback(params Assembly[])`** — explicit allowlist for `NewObject`'s `FindType` fallback. Additive across calls.
- **`WithExecutionTimeout(TimeSpan)`** + **`WithMaxStatements(int)`** — defense-in-depth defaults: 10 s / 5 000 000.
- **`TranslationOptions.MaxAstDepth`** (default 256) on `JsExpressionTranslator` — depth guard against host-crashing `StackOverflowException`.
- **`TsTranspiler.MaxParseDepth`** (default 128) — pre-parse paren/bracket/brace depth scan that rejects deeply nested input with a controlled exception.

### Changed
- **`GetValue<T>`** preserves reference identity for non-primitive types instead of routing through a JSON round-trip.
- **`ExecuteAsync` exception filter** — `Stop()`-driven cancellation stays silent; timeouts propagate.

### Removed
- **`exit()` JS global** — left the engine permanently dead. Use an IIFE for early-return.
- **`NewObject` AppDomain-wide assembly walk** — resolution is now strictly `TypeAliases ∪ EnableNewObjectAssemblyFallback` assemblies.

## [3.3.0]

### Added
- **`Type.IsOneOf(value, ['a','b',…])`** — shorthand for multiple OR'd `Type.Is` calls. Expands to an `OrElse` chain in LINQ. Monaco narrows to the correct union type via a conditional-type overload in the generated `.d.ts`.
- **`AddDiscriminatorMappings<T>(propertyName, ...)`** — property-based discriminator mappings; the property name is specified once. With view types for Monaco narrowing: `("ParticipantType", ("person", typeof(PersonView)), …)`. Without: `("ParticipantType", "person", "company")`. LINQ generates `p.ParticipantType == "person"` — works with any provider including Marten.
- **`JsEngine` auto-registers `Type` global** when discriminator mappings are configured.
- **`DefinitionBuilder.AddDiscriminatorMappings`** — emits `declare const Type` with per-value `Is()` overloads and a conditional-type `IsOneOf<D>()` overload. `TsDefinitionService` mirrors engine mappings automatically in the DI path.
- **Namespace-mapping fallback in `Type.Is`** — lookup order: explicit mapping → type alias → namespace-mapped name.

### Changed
- `DiscriminatorMapping` is now a `class` with nullable `ConcreteType` / `PropertyName`; `IsMatch` delegate is pre-compiled in the constructor.

### Removed
- `DiscriminatorEntry` — replaced by native C# tuple and `string` syntax in the new builder overloads.

### Fixed
- **AND-narrowing for combined discriminator mappings.** `Type.Is(p, 'person') && p.Email.endsWith(...)` now resolves subtype-only properties correctly for combined mappings.
- **Optional chaining (`?.`) in AND-narrowing predicates.** `Type.Is(p, 'person') && p.Email?.endsWith(...)` now resolves subtype-only properties correctly. `VisitChainElement` now mirrors the fallback in `VisitMember`. Workaround (`p.Email && p.Email.endsWith(...)`) no longer needed.
- **`DefinitionBuilder` maps collections to TypeScript array types.** `List<T>`, `IEnumerable<T>`, etc. are now `Array<T>` in the generated `.d.ts` — no more `System.Collections.Generic.List$1<T>`. `.some()`, `.includes()`, `.filter()` no longer show as Monaco errors. `IReadOnlyList<T>` / `IReadOnlyCollection<T>` → `ReadonlyArray<T>`; `Dictionary<K,V>` / `IDictionary<K,V>` → `Record<K,V>`.

## [3.2.0]

### Added
- **`Type.IsOneOf(value, ['a','b'])`** — shorthand for multiple OR'd `Type.Is` calls. Expands to an `OrElse` chain in LINQ. Monaco narrows to the correct union type via a conditional-type overload in the generated `.d.ts`.
- **`AddDiscriminatorMappings<T>(propertyName, ...)`** — property-based discriminator mappings; the property name is specified once. With view types for Monaco narrowing: `("ParticipantType", ("person", typeof(PersonView)), …)`. Without: `("ParticipantType", "person", "company")`. LINQ generates `p.ParticipantType == "person"` — works with any provider including Marten.
- **`JsEngine` auto-registers `Type` global** when discriminator mappings are configured.
- **`DefinitionBuilder.AddDiscriminatorMappings`** — emits `declare const Type` with per-value `Is()` overloads and a conditional-type `IsOneOf<D>()` overload. `TsDefinitionService` mirrors engine mappings automatically in the DI path.
- **Namespace-mapping fallback in `Type.Is`** — lookup order: explicit mapping → type alias → namespace-mapped name.

### Changed
- `DiscriminatorMapping` is now a `class` with nullable `ConcreteType` / `PropertyName`; `IsMatch` delegate is pre-compiled in the constructor.

### Removed
- `DiscriminatorEntry` — replaced by native C# tuple and `string` syntax in the new builder overloads.

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
