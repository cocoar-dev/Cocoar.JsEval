# Changelog

All notable changes to this project will be documented in this file.

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
- **Cocoar.JsEval.Expressions** — Expression Tree helpers for LINQ-compatible filters
  - `ExpressionHelper` — Equal, NotEqual, Contains/IN, StartsWith, EndsWith, StringContains, GreaterThan(OrEqual), LessThan(OrEqual), IsNull, IsNotNull, Any, HasAny, OrderBy, And, Or, Not
  - `EnumExpressionHelper` — three strategies: `Equals` (native), `EqualsAsString`, `EqualsAsInt`
  - `FilterBuilder<T>` — fluent builder with WhereIf, WhereAny, WhereEnum, Build (AND), BuildOr (OR)
  - `PropertyPath` — dotted path resolution for nested properties (e.g., `"Customer.Name"`)
  - `ListHolder<T>` — closure pattern wrapper for LINQ-provider-compatible Contains/IN
  - `ExpressionRewriter` — strips `Convert(enum, Int32)` from expression trees
