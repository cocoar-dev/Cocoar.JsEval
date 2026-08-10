# Changelog

All notable changes to this project will be documented in this file.

## [5.0.0] — Untrusted-script hardening: interop restrictions, Jint 4.15.3

**Breaking.** Passing an object to a script grants more than the object — it grants everything reachable from it. `AllowOnly` and `DenyTypes` make that surface a decision instead of a consequence, and `Sandboxed()` locks the runtime down. All three are opt-in. The upgrade to Jint 4.15.3 changes how a script sees a CLR array, which is the only change that can affect existing scripts.

### Added

- **`Sandboxed()`** — strict mode, no `eval`/`Function`, invariant culture/UTC, memory/recursion/stack/array/regex limits, no `GetType()` or reflection, no shared-memory primitives, frozen prototypes. Latches in **both** directions: `EnableFetch()` before or after it throws, so the guarantee never depends on builder order. CLR interop stays on — it hardens the runtime, it does **not** narrow the object graph.
- **`AllowOnly(a => a.Member(...).Method(...).Type<T>())`** — declares the members a script may reach; everything else stops existing for it, on nested objects and through `Object.keys`, `for..in` and `JSON.stringify` alike. Members are named through expressions, so a rename is a compile error rather than a silently narrower sandbox.
- **`DenyTypes(params Type[])`** — refuses types outright, checked on the declared *and* the runtime type, so a member declared as `object` cannot smuggle one through. Do not pass `typeof(object)`; it denies everything.
- **`ConfigureJint(Action<Jint.Options>)`** — reaches Jint options that only apply at construction time, which `RegisterEngineConfigurator` cannot.
- **`WithMaxJsonDepth(int)`** on `JsEngineOptions` (default 512).
- **`TranslationOptions.IdentifierResolver`** (Cocoar.JsEval.Linq) — resolves a free identifier in a rule to a host object, which is how a rule reaches an imported module (its binding is module-scoped, while identifier resolution reads globals). A call on a resolved object whose arguments are all constant is folded during translation.

### Changed

- **Jint 4.8.0 → 4.15.3.** A CLR `T[]` is now a live view rather than a copy. Index writes, `sort` and `reverse` reach the underlying array instead of being **silently discarded**; `push` and `length =` throw, because a fixed-size array cannot honour them. `Array.isArray(hostArray)` is now `false`, and `host.Tags === host.Tags` is now `true`. `List<T>` keeps full mutability and is unaffected, as are `map`, `filter`, `join`, `slice`, spread, `for..of`, `Object.keys` and `JSON.stringify`.
- **Generated `.d.ts`** declares an outbound CLR array as `ClrArray<T>` (helper in `global.d.ts`) so TypeScript rejects `push` instead of allowing it. Inbound parameters stay `T[]`.
- **A module exception keeps its own message** instead of surfacing as `TargetInvocationException`'s "Exception has been thrown by the target of an invocation".

### Security

- **`Scriban` 7.1.0 → 7.2.6** (`Cocoar.JsEval.Module.Template`) and **`AngleSharp` 1.4.0 → 1.7.1** (`Cocoar.JsEval.Module.AngleSharp`) — both shipped versions carried published advisories (Scriban 2× high / 2× moderate, AngleSharp 1× moderate). Consumers of those two module packages get the updated dependency transitively.
- Microsoft.Extensions.* and the SQLite/EF packages moved to 10.0.10, `Microsoft.SourceLink.GitHub` to 10.0.301. `Marten` 8.33.0 → 9.11.0 and a `SQLitePCLRaw` pin affect only the test and experiment projects, which are not packaged. The solution now builds with no vulnerability warnings.

### Known limitation

- **`count`, `find` and `any` do not work on Marten 9.** These three `JsLinqExtensions` methods are terminal and execute synchronously, because a JavaScript expression has to return a value. Marten 9 permits asynchronous data access only and throws `NotSupportedException`. `where`, `orderBy` and `thenBy` are lazy and unaffected, as is Marten 8 and every provider that allows synchronous execution (EF Core, LINQ2DB, in-memory). Build the query in JS and terminate it in C# — see the [LINQ guide](/guide/linq).

### Fixed

- **A script could terminate the host process.** Jint's JSON serializer recurses per level, so a ~120-byte script nesting a few thousand objects exhausted the .NET stack and killed the process with an uncatchable `StackOverflowException` — while staying inside every configured limit. `GetValue<T>` and `JsonStringify` now check the shape first and throw `InvalidOperationException`. The guard runs before `ToObject()`, which recurses too; wrapped host objects are exempt so reference identity is preserved.
- **A module could not accept a `JsValue` parameter.** Every argument went through `ToObject()`, which turns a JS arrow function into a delegate the parameter then rejected — so a module could not receive a rule to translate.

### Migration

Existing code keeps working unchanged. Review scripts only if they *write to* a CLR `T[]`:

```csharp
// New: decide the reachable surface instead of inheriting it
services.AddJsEval(b => b
    .Sandboxed()
    .AllowOnly(a => a.Member((Customer c) => c.Name))
    .DenyTypes(typeof(DbContext)));
```

```js
// Silently lost before, now takes effect on the host's array:
host.Tags.sort();
// Worked before (and lost the write), now throws:
host.Tags.push('x');
// Was true before, now false:
Array.isArray(host.Tags);
```

## [4.1.0] — Constructor-pure JsEngine (Wolverine 6 / static-analysis friendly)

`JsEngine` no longer takes `IServiceProvider` directly, and `AddJsEval` now registers both `JsEngine` and `IJsModuleBuilder` **type-based** rather than via opaque lambda factories. Apps on Wolverine 6's strict `ServiceLocationPolicy.NotAllowed` default can inject `JsEngine` into handlers without per-app `AlwaysUseServiceLocationFor<T>` allowlist entries. Consumers using `services.AddJsEval(...)` are unaffected.

### Added

- **`IJsModuleBuilder` / `JsModuleBuilder`** in `Cocoar.JsEval` — owns the `IServiceProvider`-based module activation (constructor-parameter resolution via DI + `ActivatorUtilities`). Registered scoped by `AddJsEval`.

### Changed

- **`JsEngine` constructor** is now `JsEngine(IJsModuleRegistry, IJsModuleBuilder, JsEngineOptions, ILogger<JsEngine>?)` — no more `IServiceProvider`. Only direct `new JsEngine(...)` callers need to update.
- **`IJsModuleRegistry`** reduced to `GetRegisteredModuleDefinitions()`. The `BuildModuleInstance` / `BuildSingleModuleInstance` methods moved to `IJsModuleBuilder`. `TsDefinitionService` and other registry consumers are unchanged.
- **DI registration in `AddJsEval`** switched from lambda-factory closures to type-based registration (`AddScoped<JsEngine>()`, `TryAddScoped<IJsModuleBuilder, JsModuleBuilder>()`). Wolverine 6's strict codegen rejects opaque `ImplementationFactory` closures regardless of how clean the underlying ctor is — type-based registration lets the static analyzer walk the ctor like any other service.

### Migration (only for direct ctor users)

```csharp
// Before (4.0):
new JsEngine(serviceProvider, moduleRegistry, options, logger);

// After (4.1):
new JsEngine(moduleRegistry, new JsModuleBuilder(serviceProvider, moduleRegistry), options, logger);
```

## [4.0.0] — Security hardening: minimal-mode defaults

**Breaking.** Unsafe-by-default JS globals are now off by default. See [SECURITY.md](SECURITY.md) for the threat model.

### Added

- **Opt-in builder flags** for previously-default globals: `EnableNewObject()`, `EnableRequire()`, `EnableTimers()`, `EnableConsole()`. Symmetrical with the existing `EnableFetch()` / `EnableDebugMode()`.
- **`EnableNewObjectAssemblyFallback(params Assembly[])`** — explicit allowlist for the `NewObject` `FindType` fallback. Additive across calls. Without it, `NewObject` is alias-only.
- **`WithExecutionTimeout(TimeSpan)`** and **`WithMaxStatements(int)`** on the builder. Defense-in-depth defaults: 10 s / 5 000 000. Pass `Timeout.InfiniteTimeSpan` / `0` to disable.
- **`TranslationOptions.MaxAstDepth`** (default 256) on `JsExpressionTranslator` — depth guard that prevents host-crashing `StackOverflowException` on deeply nested scripts.
- **`TsTranspiler.MaxParseDepth`** (default 128) — pre-parse paren/bracket/brace depth scan that rejects deeply nested input with a controlled `TsTranspileException` before it can crash the host process. The TypeScript compiler runs as JavaScript inside Jint, which amplifies stack cost ~10× and exhausts the .NET stack at ~300 levels — long before Acornima's own 5000-cap engages.

### Changed

- **`GetValue<T>`** preserves reference identity for non-primitive types (`IQueryable<T>`, custom classes set via `SetValue`) instead of routing through a JSON round-trip.
- **`ExecuteAsync` exception filter:** `Stop()`-driven cancellation stays silent; timeouts (`TimeoutException`) propagate.

### Removed

- **`exit()` JS global** — left the engine permanently dead. Use an IIFE for early-return: `(() => { if (cond) return early; … })()`.
- **`NewObject` AppDomain-wide assembly walk** via `Cocoar.Reflectensions.TypeHelper.FindType` is gone. Resolution is now strictly `TypeAliases ∪ EnableNewObjectAssemblyFallback` assemblies.

### Migration

```csharp
// Before (5.x — implicit defaults)
services.AddJsEval();

// After (4.0 — explicit opt-in for what your scripts actually need)
services.AddJsEval(b => b
    .EnableNewObject()
    .EnableNewObjectAssemblyFallback(typeof(MyDomainType).Assembly)
    .EnableConsole()
    .EnableTimers()
    .EnableRequire());
```

For DB-stored scripts that called `exit()`, rewrite to an IIFE:

```javascript
// Before:  if (cond) exit();   later code…
// After:
(() => {
    if (cond) return;
    // later code…
})();
```

## [3.3.0]

### Added

- **`Type.IsOneOf(value, ['a','b',…])`** — shorthand for multiple OR'd `Type.Is` calls. At runtime evaluates each discriminator in order and returns `true` on the first match. In LINQ predicates the translator expands it to an `OrElse` chain. TypeScript definition emits a conditional-type overload so Monaco narrows the union type correctly (`value is PersonView | CompanyView`).

- **`AddDiscriminatorMappings<T>(propertyName, ...)`** on `JsEvalBuilder` — property-based discriminator mappings with the property name specified once at the group level. Two forms:
  - `("ParticipantType", ("person", typeof(PersonView)), ("company", typeof(CompanyView)))` — LINQ generates `p.ParticipantType == "person"`; Monaco narrows to the view type.
  - `("ParticipantType", "person", "company")` — LINQ generates property equality; `Type.Is` returns plain `boolean`.

- **`DiscriminatorMapping` constructors** for property-based discrimination:
  - `new(baseType, value, propertyName)` — property equality, no Monaco narrowing.
  - `new(baseType, value, concreteType, propertyName)` — property equality + Monaco narrowing via view type.

- **`JsEngine` auto-registers `Type` global** when `DiscriminatorMappings` are configured — no manual `engine.SetValue("Type", …)` needed.

- **`DefinitionBuilder.AddDiscriminatorMappings`** — registers discriminator overloads so `TypeScriptRenderer` emits `declare const Type` with per-value `Is()` overloads and a conditional-type `IsOneOf<D>()` overload. `TsDefinitionService` mirrors `JsEngineOptions.DiscriminatorMappings` automatically in the DI path.

- **Namespace-mapping fallback in `Type.Is`** — when no explicit mapping matches, `Type.Is` tries the namespace-mapped short name next. Lookup order: explicit mapping → type alias → namespace-mapped name.

### Changed

- **`DiscriminatorMapping` is now a `class` (was a `record`)**. Adds `PropertyName` (nullable `string`) and `ConcreteType` (nullable `Type`); `IsMatch` is a pre-compiled `Func<object, string, bool>` — no per-call reflection.

- **`TsDefinitionService`** skips property-only mappings (null `ConcreteType`) when emitting Monaco narrowing overloads.

### Removed

- **`DiscriminatorEntry`** — builder helper struct removed. The new `AddDiscriminatorMappings` overloads use native C# tuple and `string` syntax directly.

### Fixed

- **AND-narrowing for combined discriminator mappings.** `Type.Is(p, 'person') && p.Email.endsWith(...)` now resolves subtype-only properties correctly when using combined mappings (`AddDiscriminatorMappings<T>("PropName", ("person", typeof(PersonView)), …)`). Previously `CollectNarrowings` recognized only `TypeBinaryExpression` nodes — combined mappings emit `p.Prop == "value"` (`BinaryExpression{Equal}`), so no narrowing frame was ever pushed and the property lookup threw. `CollectNarrowings` now also recognizes the property-equality pattern and injects `ConcreteType` as the narrowing target. Property-only mappings (no `ConcreteType`) still throw — there is no subtype to narrow to.

- **Optional chaining (`?.`) in AND-narrowing predicates.** `Type.Is(p, 'person') && p.Email?.endsWith(...)` now resolves subtype-only properties correctly. Previously `VisitChainElement` resolved member accesses strictly against `obj.Type` (the declared base type) and never consulted the active narrowing context, so any `?.` access to a subtype-only property threw `Property 'X' not found on BaseType`. `VisitChainElement` now mirrors the fallback in `VisitMember`: if the direct lookup fails and `obj` is a narrowed parameter, `TryResolveViaIntersection` is tried next. Workaround (`p.Email && p.Email.endsWith(...)`) is no longer needed.

- **`DefinitionBuilder` maps `List<T>` and other collections to `Array<T>` in generated `.d.ts`.** Previously `List<T>`, `IEnumerable<T>`, `ICollection<T>`, `HashSet<T>`, etc. were emitted as `System.Collections.Generic.List$1<T>` — a type without `.some()`, `.includes()`, `.filter()` in Monaco, causing red underlines even though the script ran correctly at runtime. Now mapped: `List<T>` / `IList<T>` / `IEnumerable<T>` / `ICollection<T>` / `Collection<T>` / `HashSet<T>` / `ISet<T>` → `Array<T>`; `IReadOnlyList<T>` / `IReadOnlyCollection<T>` / `ReadOnlyCollection<T>` → `ReadonlyArray<T>`; `Dictionary<K,V>` / `IDictionary<K,V>` / `IReadOnlyDictionary<K,V>` → `Record<K,V>`.

## [3.2.0]

### Added
- **`JsEngine.PrepareModule(string)`** — pre-parse an ES-module script for repeated execution. Returns a thread-safe `JsPreparedModule` that can be cached globally and passed to `ExecuteAsync(JsPreparedModule)`. Avoids the per-call parse cost on fresh engines; on a pooled engine the same prepared module hits the module cache directly.
- **`JsEngine.ExecuteAsync(JsPreparedModule)`** — overload that accepts a pre-parsed module.

### Changed (behavioural)
- **`ExecuteAsync(string)` and `ExecuteAsync(JsPreparedModule)` now follow standard ES-module semantics.** Top-level code runs **once per unique script content** on a given engine. Repeated executions of the same script return the cached module namespace instead of re-parsing and re-evaluating top-level statements. This matches how ES modules work everywhere else (Node, browsers, Deno) and is the opposite of the previous "REPL-style every-call re-execution" behaviour.
  - **Migration:** scripts that relied on top-level code re-running (e.g. `export const id = Math.random()` yielding a different `id` on each call) need to be rewritten to expose per-call work as **exported functions**: `export function newId() { return Math.random(); }` invoked via `engine.InvokeFunction("newId")`. This is the idiomatic JS/TS pattern and works the same in Node and the browser.
  - The lightweight path (`Evaluate(string)` / `Evaluate(prepared)` / `EvaluateAsync(string)`) has no module system and always runs the full script — use it if you truly need per-call re-execution semantics.
- **Module cache eliminates the `__main_N__` memory leak.** The previous implementation generated a unique module name per call (`__main_0__`, `__main_1__`, …) which accumulated indefinitely in Jint's module registry on long-lived engines. The new content-addressed cache stores O(unique scripts) entries instead of O(calls).

### Performance
- **Hot-loop `import` is now ~115× faster** on a pooled engine — `ExecuteAsync(string)` with a repeated script drops from 14 µs/call to 123 ns/call (cache hit returns the module namespace directly, no parse / link / evaluate).
- **Pooled + `ExecuteAsync(prepared)`** drops from 12 µs/call to 1.3 µs/call (~9×).
- Fresh-engine numbers are unchanged (nothing to cache on first use). See `PERFORMANCE-COMPARISON.md` at the repo root for the full before/after table.

### Fixed (benchmark infrastructure)
- **`ValueBenchmarks.TaskInterop` and `EngineBenchmarks.AsyncAwait` now run successfully.** Both previously returned `NA` — the benchmark harness reused a scoped `JsEngine` across iterations but called `Dispose()` after each one, which disposed the engine's `CancellationTokenSource`; the next iteration hit `ObjectDisposedException` on any async path. Benchmarks now open a fresh DI scope per iteration, so measurements reflect a real fresh-engine cost (and async paths complete).
- **"Engine creation (cold start)" benchmark now measures what it claims.** The pre-fix number (~1.5 µs) was a DI cache lookup of a reused scoped instance, not actual engine construction. The real cold-start cost is ~9.8 µs.

## [3.1.4]

### Added
- **`Cocoar.JsEval.Linq` — `linq.d.ts` is now auto-emitted by `TsDefinitionService.GetTsDefinitions()` whenever `AddLinq()` is on the builder.** Previously the `linq` runtime global (`linq.guid('…')`, `linq.decimal('…')`, `linq.today()`, …) had no TypeScript declaration — Monaco flagged every use as `Cannot find name 'linq'` even though it worked at runtime. The hand-written `linq.d.ts` that accompanied `LinqCasts.Register` is now shipped through a new `IJsTsDefinitionContributor` interface: `AddLinq()` registers `LinqTsContributor` as a singleton, and `TsDefinitionService` merges its output into the returned dictionary. Zero configuration on the consumer side — if `AddLinq()` is called, the definition shows up. Any third-party package with its own runtime-only globals can use the same interface. Contributor output is applied last, so hosts can override bundled files if needed.
- **`JsEvalBuilder.AddTypeAlias<T>("ShortName")` and `MapNamespace(prefix, target)`** — register short-name aliases that are *simultaneously*:
  - Emitted at **root scope** in the generated `.d.ts` so Monaco's hover popup shows `CustomerView` instead of `TimeToDo.Infrastructure.Persistence.Marten.Projections.Customers.CustomerView`
  - Resolvable by `NewObject("CustomerView")` at runtime — same name works on both sides
  Cross-references between aliased/mapped types use the short name too, so `class PrincipalDirectory { Person: PersonData }` renders without the long namespace noise. Single source of truth lives on `JsEngineOptions.TypeAliases` / `JsEngineOptions.NamespaceMappings`; `TsDefinitionService` (when DI-resolved) reads the same data so the two layers cannot drift apart.
- **`DefinitionBuilder.AddType(Type, string alias)` and `DefinitionBuilder.MapNamespace(source, target)`** — the standalone-builder equivalent for consumers not going through DI. Same collision rules.
- **`IJsTsDefinitionContributor` and `JsEvalBuilder.AddTsDefinitionContributor<T>()`** — a general extension point for any package that wants to contribute `.d.ts` files without being registered as an `IJsModule`. `Cocoar.JsEval.Linq` is the first internal user.

### Changed
- **Namespace mappings respect `System.*` by default.** `MapNamespace("", "")` or `MapNamespace("TimeToDo", "")` do not re-home System types — they stay fully qualified as `System.Guid`, `System.DateTime`, etc. The FR had flagged this as the safest default; the alternative (flattening BCL types too) would produce noisy name collisions and pointless short `Guid`/`Int32`/`String` aliases that Monaco already understands via `lib.es*`.
- **Collision detection fires only when a rule is involved.** Two distinct types that naturally share a short name (e.g. nested `Span<T>.Enumerator` and `ReadOnlySpan<T>.Enumerator`, both rendered as `Enumerator$1` under `System`) keep the v3.1.3 pre-existing behavior of emitting two matching `interface` declarations which TypeScript merges. Once any alias or `MapNamespace` rule touches one of the colliding types, the resolver throws `InvalidOperationException` at render-time (or `NewObject`-map-build-time) with both source types named and three suggested fixes — so the user gets immediate feedback when shaping the output, but unconfigured consumers don't regress. Collisions between two *explicit* aliases on the same name throw at registration time, not render time.

## [3.1.3]

### Removed (breaking for direct lib.* consumers)
- **`Cocoar.JsEval.TsDefinition` no longer ships `lib.es5.d.ts` or `lib.es2015.core.d.ts` as part of `GetTsDefinitions()`.** Those were vendored copies of a very old TypeScript standard library (predated TS 4.x — `Copyright © Microsoft Corporation` header from the original 1.x-era bundle), shipped as a convenience for Monaco integrations. Two problems: (1) they were years out of date, and (2) Monaco's own TypeScript language service already loads its own version-matched libs internally — stacking ours on top could silently override fresher types. Neither this package nor any other `Cocoar.JsEval.*` package reads these files; they were pass-through resources only. `global.d.ts` (the hand-written file that declares JsEval-specific globals like `fetch`, `NewObject`, `exit`, `require`) is unchanged and still ships. Monaco consumers who relied on these should either use Monaco's built-in libs (the default), or embed current libs from `Cocoar.JsEval.TypeScript.V8.EmbeddedResources.LibFiles` (97 files, TS 6.0.2, ES-only).

### Fixed
- **`Cocoar.JsEval.TsDefinition` — Rendered `.d.ts` output is now parse-clean TypeScript.** Three independent renderer bugs produced output that the TypeScript compiler refuses, silently breaking any consumer that pipes `TsDefinitionService.GetTsDefinitions()` into a real type-check path (Monaco's worker, `ts.createProgram`, IDE language services). Verified with a `ts.createSourceFile` probe: before the fix, 5 of 9 non-`lib.` files produced by the default module set failed to parse (`System`, `Cocoar`, `SqlKata`, `Dapper`, `AngleSharp` — 465 parse errors in `System.d.ts` alone); after the fix, all 9 parse with zero diagnostics.
  - **`Task<T>` / `ValueTask<T>` emitted the generic argument twice**, producing `Promise<T><T>`. `NormalizeTypeName` baked `<T>` into the returned string *and* `BuildTypeString` appended it a second time from `TypeDefinition.GenericArguments`. Now `NormalizeTypeName` returns the bare wrapper (`Promise`) and the caller appends generic args once — matches the handling of every other generic type.
  - **`ref T` parameters and return types leaked the .NET ByRef suffix `&` into TS output** (e.g. `Current: T&`) — a TS parse error, since `&` is the intersection operator and needs a right-hand operand. The existing check used `Type.FullName.EndsWith('&')`, but `FullName` is `null` for generic type parameters like `ref T`, so the check silently missed them. Now uses `Type.IsByRef || Type.IsPointer` with `GetElementType()` to unwrap.
  - **Name-colliding types were declared twice in the same namespace.** `TypeDefinition.FromType` deduplicates by `FriendlyName` via its internal cache, so distinct `Type` inputs collapsing to the same friendly name return the same `TypeDefinition` reference — but the renderer pushed that reference into `namespace.Types` on every encounter, emitting 40+ duplicate declarations across `System.d.ts` (`Type`, `Attribute`, `AdjustmentRule`, `RuntimeTypeHandle`, …). Now dedup'd on add. Remaining same-name collisions from genuinely distinct nested types (e.g. `Span<T>.Enumerator` vs `ReadOnlySpan<T>.Enumerator`) render as duplicate `interface` declarations, which TypeScript accepts via interface merging — a separate, cosmetic issue to address later by including parent-type name in the rendered identifier.

### Changed
- **`TypeScriptRendererDefaults.NormalizeTypeName(TypeDefinition, …)` contract (direct callers only).** For `Task<T>` / `ValueTask<T>` the method now returns `"Promise"` instead of `"Promise<T>"` — the caller is expected to append the generic args from `TypeDefinition.GenericArguments`. Non-generic `Task` / `ValueTask` still return `"Promise<void>"` (unchanged, comes from `TypeMappings`). Consumers that use the bundled `TypeScriptRenderer` see no behaviour change — the final rendered `.d.ts` output for `Task<string>` is still `Promise<string>`, just composed once instead of twice.

## [3.1.2]

### Fixed
- **`Cocoar.JsEval.TsDefinition` — `Guid` is no longer forced to `string`.** The type-mapping table had `[typeof(Guid)] = "string"`, silently erasing Guid-ness at the TypeScript layer — so in Monaco, `t.CustomerId === 'abc-…'` and `t.CustomerId === linq.guid('abc-…')` looked indistinguishable (both `string === string`), and admins authoring access-policy scripts could bypass `linq.guid(…)` by accident with no editor feedback. The override is gone; `Guid` now falls through the normal rendering path and resolves to `Guid` (or `System.Guid` with namespaces enabled). Consumers who want to preserve the old behaviour can re-add the mapping explicitly via `TypeScriptRendererDefaults.TypeMappings`.

### Added
- **`Cocoar.JsEval.TypeScript` — `TsTranspiler.TranspileWithSourceMap(string)`** returns a `TsTranspileResult(Js, SourceMap, Warnings)` with the JS output, a Source Map v3 JSON string, and any non-error diagnostics. The trailing `//# sourceMappingURL=` comment is stripped from the JS so consumers can embed the map however they prefer (inline base64, sidecar file, in-memory for error-position mapping, …). Enables surfacing runtime-error positions in the original TypeScript source — useful for Monaco-backed editors where admins author scripts and a later `EvaluateExpression` failure needs to point at the right line.

### Fixed
- **`Cocoar.JsEval.TypeScript` — Transpiler now throws `TsTranspileException` on TypeScript errors instead of silently returning broken output.** 3.1.1 and earlier called `ts.transpileModule` without `reportDiagnostics: true`, so syntax errors produced a near-empty `"use strict";` string with no signal. Now a syntax error surfaces as `TsTranspileException` with a `IReadOnlyList<TsDiagnostic>` (category, TS error code, message, 1-based line/column) — structured enough to drive editor cursor-placement and error banners. Type errors like `const x: number = "oops"` are *still not reported* — `ts.transpileModule` doesn't build a full program; that's a documented limitation and would require a `createProgram`-based typechecker add-on.

## [3.1.1]

### Fixed
- **`Cocoar.JsEval.Linq` — Optional chaining in LINQ provider predicates (Marten / EF Core / LINQ2DB).** v3.1.0 emitted nested `ConditionalExpression` nodes (one per `?.` guard), which Marten's `WhereClauseParser` refuses (`BadLinqExpressionException: Whoa pardner, Marten could not parse 'IIF(...)'`). Verified end-to-end in all three sandbox projects (`JsEval.Marten.Sandbox` against Postgres, `JsEval.EfCore.Sandbox` and `JsEval.Linq2Db.Sandbox` against SQLite in-memory) with the two real-world scripts that triggered the rollback in a downstream authorization app: `(p) => p.Type === 'Person' && p.IsActive && (p.Person?.Firstname?.startsWith('A') || p.Person?.Firstname?.startsWith('L') || p.Person?.Firstname?.startsWith('P'))` and `(t) => t.Customer?.Id === linq.guid('...') || …`. All three script variants (natural v3.1.1 syntax, v3.1.0 `=== true` style, hand-rolled workaround) now return identical rows on every provider.

### Changed
- **`Cocoar.JsEval.Linq` — Boolean-context optional chains flatten to pure `&&`-chains.** In any boolean context (lambda body returning `bool`, operand of `!`, `&&`, `||`, ternary test), an optional chain whose body is `bool` rewrites to `g1 != null && g2 != null && body` — zero `IIF` nodes, byte-identical to hand-written C#, translated natively by every mainstream LINQ provider. Non-bool chains (e.g. `(u) => u.Address?.City` as `string`) keep a single positive-test IIF.
- **`Cocoar.JsEval.Linq` — Optional chains in binary comparisons flatten too.** `(t) => t.Customer?.Id === linq.guid(...)` now emits `t.Customer != null && t.Customer.Id == guid` directly — no IIF-inside-comparison, which every mainstream provider refuses. Applies to `==` / `===` / `<` / `<=` / `>` / `>=` whenever the other operand is a known-non-null constant (literal, `linq.*` typed literal, host-set closure pointing at a non-null value).
- **`Cocoar.JsEval.Linq` — Redundant `x === true` / `x === false` collapse.** `u.IsActive === true` → `u.IsActive`, `u.IsActive === false` → `!u.IsActive`, and the `!==` variants likewise. Only applies when the non-constant side is plain `bool` (not `bool?` — those keep lifted semantics). Marten in particular can translate `StartsWith("x")` but not `StartsWith("x") == true`; this makes v3.1.0-style `=== true` scripts work unchanged.
- **`Cocoar.JsEval.Linq` — `bool?` → `bool` coercion in boolean contexts (JS-truthy semantics).** Optional chaining inside `Where(...)`-style predicates, negation (`!`), logical `&&` / `||`, and ternary test position no longer require an explicit `=== true`. The translator normalizes `Nullable<bool>` to `bool` by treating `null` as `false` at each boolean-context site — matching JS where `undefined`/`null` are falsy. `where(p => p.Person?.Name.startsWith('A'))` just works; `where(p => !p.Person?.Name.startsWith('A'))` correctly returns rows where `Person` is null (JS: `!undefined === true`). Non-nullable `bool` operands pass through unchanged.

## [3.1.0]

### Added
- **`Cocoar.JsEval.Linq` — Optional chaining (`?.`) in predicates.** `(t) => t.Customer?.Label.startsWith('A')` now translates to a null-safe expression tree. Each `?.` step short-circuits the rest of the chain to `null` if the guarded target is null. Skips the guard when the target is a non-nullable value type (never null). Works for both LINQ-provider translation and in-memory `Expression.Compile()` evaluation — removes the need for `x != null && x.y…` boilerplate in auto-membership and similar scenarios.
- **`Cocoar.JsEval.Linq` — Nullish coalescing (`??`) in predicates.** `(u) => u.Name ?? 'anon'` translates to `Expression.Coalesce(…)`. Combines naturally with optional chaining: `(u) => (u.Address?.City ?? '') === 'Vienna'`.

### Changed (behaviour)
- **`AddJsEval` now registers `JsEngine` as scoped** instead of transient. Multiple services resolved in the same DI scope (e.g. per HTTP request) now share one engine — globals set via `SetValue` are visible across collaborators, and the "Jint engine is not thread-safe" contract is enforced at DI level rather than left to convention. Consumers that need isolated engines can still instantiate with `new JsEngine(…)`.

## [3.0.0]

v3.0.0 rolls up v2.0.0 plus a small but breaking API cleanup surfaced by first-adopter integration. v2.0.0 was unlisted from NuGet the same day; install v3.0.0 directly.

### Breaking (v2.0.0 → v3.0.0)
- **`IJsEngine` interface removed.** The interface claimed an engine-swap abstraction that the library does not actually support (the whole codebase depends on Jint's `JsValue`/`ScriptFunction`/Acornima AST). `JsEngine` is now the public contract.
  - **Migration:** replace `sp.GetRequiredService<IJsEngine>()` with `sp.GetRequiredService<JsEngine>()`; replace `IJsEngine` parameter/field types with `JsEngine`. No other behaviour changed.

### Added in v3.0.0 (on top of v2.0.0)
- **`JsEngine.UnderlyingEngine { get; }`** — direct public access to the underlying `Jint.Engine`, so advanced consumers can reach Jint APIs without reflection.
- **`JsEngine.EvaluateExpression(string): JsValue`** — expression-semantics evaluation that returns the resulting `JsValue` (the existing `Evaluate(string)` remains statement-semantics / `void`). The common "evaluate a JS expression, hand it to the Linq translator" flow no longer needs a `__result` global or reflection hack.
- **`Cocoar.JsEval.Linq` convenience overloads** — `JsExpressionTranslator.Translate` / `TranslateLambda` / `JsLinqContext.Scope` accept `JsEngine` directly (in addition to the existing `Jint.Engine` overloads). Consumers no longer need to touch Jint directly for typical scenarios.

### Breaking (v1.0.0 → v2.0.0, carried into v3.0.0)
- **`Cocoar.JsEval.Expressions` package removed.** Its string-DSL expression builder (`ExpressionHelper.Equal("path", value)`, `FilterBuilder`, …) is superseded by `Cocoar.JsEval.Linq`, which produces the same Expression Trees from JS/TS predicates written in natural syntax. Internal utilities from the old package (`PropertyPath`, `ListHolder`, `EnumExpressionHelper`, `ExpressionRewriter`) were migrated into `Cocoar.JsEval.Linq/Building/` as implementation details of the translator.
  - **Migration:** rewrite `ExpressionHelper.Equal<T, V>("Name", value)` as JS — `users.where(u => u.Name === value)` — or inline it in a C# source lambda. For dotted paths `ExpressionHelper.StartsWith<T>("Customer.Name", "A")` becomes `users.where(u => u.Customer.Name.startsWith('A'))`.

### Added (originally in v2.0.0, included in v3.0.0)
- **Cocoar.JsEval.Linq** — Translates JS arrow functions into real .NET Expression Trees
  - `JsExpressionTranslator` — walks Jint/Acornima AST, produces `Expression<Func<T, TResult>>`; `TranslateLambda<T>` overload infers `TResult` from body (for `OrderBy` key selectors etc.)
  - `JsLinqExtensions` — natural-name `Where` / `Find` / `Count` / `Any` / `OrderBy` / `OrderByDescending` / `ThenBy` / `ThenByDescending` on `IQueryable<T>` (register with `AddLinq()`)
  - `JsLinqContext.Scope(engine)` — ambient engine for closure resolution across JS calls; `CurrentEngine` / `CurrentOptions` are public for custom wrappers
  - `IJsMethodMap` / `DefaultJsMethodMap` / `CompositeJsMethodMap` — pluggable method translation
  - `ExpressionDependencyCollector` + `PropertyDependencies` — extract which properties a query touches (for reactive re-execution, cache invalidation)
  - Byte-identical SQL to hand-written C# source lambdas, verified against Marten, EF Core, LINQ2DB
  - `linq.decimal('...')` / `linq.double` / `linq.int` / `linq.long` / `linq.date` / `linq.dateUtc` / `linq.dateOffset` / `linq.dateOnly` / `linq.timeOnly` / `linq.timeSpan` / `linq.guid` — typed literal DSL. The translator intercepts these at AST level to emit precision-preserving `Expression.Constant` nodes (bypassing Jint's runtime value marshalling)
  - `linq.today()` / `linq.now()` / `linq.utcNow()` / `linq.todayUtc()` — zero-arg helpers that capture the current date/time at translation time; combine with `AddDays`/`AddHours` etc. for relative predicates like `t => t.DueDate < linq.today().AddDays(7)`
  - String `indexOf` maps to `string.IndexOf` (completes the string method-map alongside `startsWith`/`endsWith`/`includes`/`toLower`/`toUpper`/`trim`)
  - **C# LINQ aliases** accepted alongside JS names: `arr.Any`/`All`/`Where`/`Select`/`FirstOrDefault`/`Contains` on arrays (mirroring `some`/`every`/`filter`/`map`/`find`/`includes`), plus `str.Contains`/`StartsWith`/`EndsWith`/`ToLower`/`ToUpper`/`Trim` on strings. Both styles produce byte-identical Expression trees
  - `LinqTypeScriptDefinition.Read()` / `.WriteTo(path)` exposes an embedded TypeScript declaration file (`cocoar-jseval-linq.d.ts`) that uses TS declaration merging to add the C# LINQ aliases to `String` and `Array<T>` for Monaco/VS Code IntelliSense
  - **Enum coercion** — when a binary expression compares an enum property to a string or numeric literal (`u.Status === 'Active'`, `u.Status === 1`), the translator auto-coerces the literal into a typed enum `Expression.Constant`. String matching is case-insensitive; output is the ORM-neutral native form (no implicit `Convert(enum, Int32)` wrapper), compatible with both int-stored and string-stored enums
  - User-defined `implicit operator` conversions are automatically applied when binary expression sides mismatch — enables wrappers like `CsDateTime` to compare transparently against their underlying type
  - Constant-folding on unary numeric negation (`-7` becomes `Constant(-7)` instead of `Negate(Constant(7))`) — required for Marten compatibility
  - Internal `ReflectionCache` for `GetProperty` / `GetMethod` / `GetImplicitCastMethodTo` / `MakeGenericMethod` lookups — ~65% faster + 74% fewer allocations for nested-lambda predicates in hot loops
- **Cocoar.JsEval.Engine** additions:
  - `CsDateTime` class with fluent `AddDays` / `AddMonths` / `AddYears` / etc. API, implicit operators to/from `DateTime`, comparison operators, static factories (`Now`, `UtcNow`, `Today`, `Parse`, `ParseUtc`, `From(...)`, `FromUnixSeconds`, `FromUnixMilliseconds`)
  - `JsEvalBuilder.EnableCsDateTime()` — opt-in registration of `CsDateTime` as JS global
  - `JsEvalBuilder.RegisterEngineConfigurator(Action<Engine>)` — generic extension point for add-on packages to register their own globals

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
- `.d.ts` generation for IntelliSense support (`TsDefinitionService`)
- `IJsEngine` interface for testability and mocking
- Fluent builder pattern: `AddJsEval(b => b.EnableFetch().AddModule<T>())`
- Engine reuse: both `ExecuteAsync` and `Evaluate` support multiple calls on the same engine
- VitePress documentation website
