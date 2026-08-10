# Security Policy

## Supported versions
The latest main branch and the most recent release are supported for security fixes.

## Reporting a vulnerability
Please do not open public issues for potential vulnerabilities. Instead:
- Email: bwi@cocoar.dev (or use your private channel if you have one)
- Include a minimal reproduction, impact assessment, and affected versions/commits.
- We aim to acknowledge reports within 72 hours.

## Disclosure
We prefer coordinated disclosure. After a fix is available, we'll publish release notes with mitigation guidance.

## Threat model

`Cocoar.JsEval` provides two distinct execution surfaces:

- **`JsEngine`** is both the host-integration and the untrusted-rule surface. Its powerful CLR and module
  capabilities are opt-in, but any CLR object passed through `SetValue` exposes
  its public interop surface — and, transitively, everything reachable from it —
  and must therefore be considered a granted capability. `AllowOnly` narrows that
  to a declared set of members, `DenyTypes` rules out types outright (checked
  both on the declared type and on the runtime type, so a member declared as
  `object` cannot smuggle one through), and `Sandboxed()` locks the runtime down
  and refuses any later call that would widen it again. The three are
  independent: `Sandboxed()` governs what a script can do on its own, `AllowOnly`
  what it can reach. An untrusted script wants both — `Sandboxed()` alone leaves
  the object graph of anything passed in fully reachable. Modules are the
  capability channel; a module that returns an internal service hands out its
  object graph unless `AllowOnly` narrows it.
- **`JsExpressionTranslator`** (Cocoar.JsEval.Linq) turns a JS arrow function
  into an expression tree without executing it as JavaScript — but the tree is
  evaluated later by the LINQ provider. Opening a `JsLinqContext.Scope` makes
  every **global** of the scoped engine resolvable from a rule, and members are
  then resolved by reflection, so an object passed to `JsEngine.SetValue`
  becomes callable when the query runs. Module imports are not reachable this
  way — closure resolution reads globals, and an `import` binding is not one. With no scope open, closure resolution is off and a rule
  reaches nothing beyond the queried entity — whose every public property is
  reachable, which is why untrusted rules should query a DTO rather than a
  persistence entity. See the LINQ guide for the full recommendation.

Globals that grant access to host primitives (`NewObject`, `require`, `setTimeout`, `setInterval`, `console`) are off on `JsEngine` until the consumer explicitly opts in via the corresponding builder flag. `Sandboxed()` refuses every one of those grants, in either order.

Defense-in-depth guards are on by default:

- **Execution timeout** — 10 s wall-clock per call (`WithExecutionTimeout`). Surfaces as `System.TimeoutException`.
- **Statement count cap** — 5 000 000 statements per call (`WithMaxStatements`). Surfaces as `Jint.Runtime.StatementsCountOverflowException`.
- **Translator depth cap** — 256-deep AST recursion in `JsExpressionTranslator` (`TranslationOptions.MaxAstDepth`). Throws `InvalidOperationException` instead of letting the host process crash with `StackOverflowException`.
- **TS-transpiler depth scan** — pre-parse paren/bracket/brace scan in `TsTranspiler` (`TsTranspiler.MaxParseDepth`, default 128). Rejects deeply nested input with a controlled `TsTranspileException` before the embedded TypeScript compiler (running as JavaScript inside Jint) can exhaust the .NET stack at ~300 levels.

`Sandboxed()` additionally sets strict mode, disables string compilation
(`eval` / `Function`), blocks `GetType()`, reflection and CLR assembly access,
removes the shared-memory primitives, freezes built-in prototypes, fixes
culture/timezone to invariant/UTC, and applies memory, recursion,
execution-stack, array and regex limits. CLR interop itself stays on — real
objects and real method calls are the point of the surface; `AllowOnly` decides
which of them a script may reach.

- **Nesting-depth cap** — 512 levels by default (`JsEngineOptions.WithMaxJsonDepth`), checked before `GetValue<T>` and `JsonStringify` convert a value. Both paths recurse per level, so without it a script nesting a few thousand deep terminated the host process with an uncatchable `StackOverflowException` while staying inside every other limit.

**Known limitation — memory is not fully bounded in-process.** `MemoryLimitBytes` is checked between statements, so allocation spread over a loop is caught, but a single large allocation is not: `'x'.repeat(800 * 1024 * 1024)` commits the whole string before any limit observes it. The run then fails, but the memory was already taken, and no Jint setting closes this. Run untrusted input in a process with an OS-level memory cap.

There is **no sandbox isolation at the OS level**. The library cannot defend against vulnerabilities in Jint itself, in the host's allowlisted assemblies, or in code that the consumer chooses to expose via `EnableNewObjectAssemblyFallback`. Treat the library as a hardening layer, not a sandbox replacement.

## Per-trust-level recommendations

| Trust level | Author | Recommended configuration |
|---|---|---|
| **High** (you wrote the script) | App developer | Enable whatever globals the script needs. Constraints can be relaxed (`WithExecutionTimeout(Timeout.InfiniteTimeSpan)`) if scripts run unattended. |
| **Medium** (admin-authored, audited) | Trusted operator | Default constraints. Enable only the globals the script needs (`EnableConsole()` + targeted `EnableNewObjectAssemblyFallback(typeof(YourType).Assembly)`). Avoid `EnableRequire()` and `EnableTimers()` unless required. |
| **Low** (tenant- or end-user-authored) | Untrusted | `Sandboxed()` for the runtime plus `AllowOnly` for the reachable surface, and `DenyTypes` for types that must never cross. Pass DTOs rather than persistence entities, and validate the result before applying changes. One engine per script source — state persists for the engine's lifetime. For a hard isolation boundary, execute it in a restricted worker process as well. If such authors also write LINQ rules, translate them with no `JsLinqContext.Scope` open (or a bare engine) and query a DTO rather than a persistence entity. |

## Reporting expectations

If you find a way to bypass any of the documented opt-in flags or constraints — or to crash the host process with a script — please report it via the channel above before disclosing publicly.
