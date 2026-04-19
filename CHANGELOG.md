# Changelog

All notable changes to this project will be documented in this file.

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
