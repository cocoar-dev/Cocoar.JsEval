# Changelog

All notable changes to this project will be documented in this file.

## [2.0.0]

### Breaking
- **`Cocoar.JsEval.Expressions` package removed.** Its string-DSL expression builder (`ExpressionHelper.Equal("path", value)`, `FilterBuilder`, …) is superseded by `Cocoar.JsEval.Linq`, which produces the same Expression Trees from JS/TS predicates written in natural syntax. Internal utilities from the old package (`PropertyPath`, `ListHolder`, `EnumExpressionHelper`, `ExpressionRewriter`) were migrated into `Cocoar.JsEval.Linq/Building/` as implementation details of the translator.
  - **Migration:** rewrite `ExpressionHelper.Equal<T, V>("Name", value)` as JS — `users.where(u => u.Name === value)` — or inline it in a C# source lambda. For dotted paths `ExpressionHelper.StartsWith<T>("Customer.Name", "A")` becomes `users.where(u => u.Customer.Name.startsWith('A'))`.

### Added
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
