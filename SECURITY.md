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

`Cocoar.JsEval` evaluates JavaScript supplied by an external author against an in-process Jint runtime. The library treats scripts as **untrusted by default**: globals that grant access to host primitives (`NewObject`, `require`, `setTimeout`, `setInterval`, `console`) are off until the consumer explicitly opts in via the corresponding builder flag.

Defense-in-depth guards are on by default:

- **Execution timeout** — 10 s wall-clock per call (`WithExecutionTimeout`). Surfaces as `System.TimeoutException`.
- **Statement count cap** — 5 000 000 statements per call (`WithMaxStatements`). Surfaces as `Jint.Runtime.StatementsCountOverflowException`.
- **Translator depth cap** — 256-deep AST recursion in `JsExpressionTranslator` (`TranslationOptions.MaxAstDepth`). Throws `InvalidOperationException` instead of letting the host process crash with `StackOverflowException`.
- **TS-transpiler depth scan** — pre-parse paren/bracket/brace scan in `TsTranspiler` (`TsTranspiler.MaxParseDepth`, default 128). Rejects deeply nested input with a controlled `TsTranspileException` before the embedded TypeScript compiler (running as JavaScript inside Jint) can exhaust the .NET stack at ~300 levels.

There is **no sandbox isolation at the OS level**. The library cannot defend against vulnerabilities in Jint itself, in the host's allowlisted assemblies, or in code that the consumer chooses to expose via `EnableNewObjectAssemblyFallback`. Treat the library as a hardening layer, not a sandbox replacement.

## Per-trust-level recommendations

| Trust level | Author | Recommended configuration |
|---|---|---|
| **High** (you wrote the script) | App developer | Enable whatever globals the script needs. Constraints can be relaxed (`WithExecutionTimeout(Timeout.InfiniteTimeSpan)`) if scripts run unattended. |
| **Medium** (admin-authored, audited) | Trusted operator | Default constraints. Enable only the globals the script needs (`EnableConsole()` + targeted `EnableNewObjectAssemblyFallback(typeof(YourType).Assembly)`). Avoid `EnableRequire()` and `EnableTimers()` unless required. |
| **Low** (tenant- or end-user-authored) | Untrusted | Defaults only. Do **not** call any `Enable*` flag beyond what the script logically needs. Tighten `WithExecutionTimeout` to seconds; set `WithMaxStatements` to a workload-appropriate cap. Run inside a host-level wall-clock budget too — JsEval's constraints are belt-and-suspenders, not sole defense. |

## Reporting expectations

If you find a way to bypass any of the documented opt-in flags or constraints — or to crash the host process with a script — please report it via the channel above before disclosing publicly.
