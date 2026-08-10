# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build
dotnet build src/Cocoar.JsEval.slnx

# Run all tests (excluding network-dependent fetch tests)
dotnet test src/Cocoar.JsEval.slnx --filter "FullyQualifiedName!~Fetch"

# Run specific test project
dotnet test src/Tests/JsEval.Tests.Engine

# Run specific test by name pattern
dotnet test src/Cocoar.JsEval.slnx --filter "FullyQualifiedName~TestMethodName"

# Marten integration tests (sandboxed rule -> real SQL). These SKIP unless a
# PostgreSQL answers. Local dev DB is port 5433; it uses a real password, so the
# connection string has to be supplied — the 5433 default only matches a
# plain-Docker Postgres.
JSEVAL_PG_CONNSTRING="Host=localhost;Port=5433;Username=postgres;Password=...;Database=postgres" \
  dotnet test src/Tests/JsEval.Tests.Marten

# Run benchmarks
cd src/Benchmarks/JsEval.Benchmarks && dotnet run -c Release -- --filter "*EngineBenchmarks*"

# Pack NuGet packages
dotnet pack src/Cocoar.JsEval.slnx -c Release

# Website dev server
cd website && npm run dev
```

## Architecture Overview

**Cocoar.JsEval** is a JavaScript/TypeScript execution library for .NET 10, built on Jint 4.15.3 (ES2025).

### Core Packages

- **Cocoar.JsEval** (`src/JsEval/`) — Core interfaces, helpers, JsFunction, IJsModule, JsModuleRegistry
- **Cocoar.JsEval.Engine** (`src/JsEval.Engine/`) — JsEngine, JsEvalBuilder, fetch(), `CsDateTime`, DI registration
- **Cocoar.JsEval.TypeScript** (`src/JsEval.TypeScript/`) — TsTranspiler with embedded TypeScript 6.0 compiler, engine pooling
- **Cocoar.JsEval.TsDefinition** (`src/JsEval.TsDefinition/`) — .d.ts generation from C# types for IntelliSense
- **Cocoar.JsEval.Linq** (`src/JsEval.Linq/`) — JS arrow function → `Expression<Func<T, TResult>>` translator for any `IQueryable<T>` provider (Marten/EF/LINQ2DB/…), with `linq.*` typed literals, ordering extensions, property-dependency collector, and `ReflectionCache` for hot-loops

### Modules (`src/Modules/`)

8 optional modules: Common, HTTP, Database, SMTP, Template, AngleSharp, Logging, VirtualFileSystem. Module names are **lowercase** in JS imports (e.g., `import * as http from 'http'`).

### Execution Methods

| Method | Module System | async | Use Case |
|---|:---:|:---:|---|
| `ExecuteAsync(string)` | Yes | Yes | **Standard** — use when you don't know what the script contains |
| `ExecuteAsync(JsPreparedModule)` | Yes | Yes | Max performance for modules — pre-parsed, ~9× faster on pooled engine |
| `Evaluate(string)` | No | No | Lightweight sync — when you control the script |
| `Evaluate(JsPreparedScript)` | No | No | Max performance — pre-parsed, reusable |
| `EvaluateAsync(string)` | No | Yes | Lightweight async — no modules but needs await |

### DI Registration

```csharp
services.AddJsEval(b => b
    .EnableFetch()
    .AddModule<HttpModule>()
    .AddModule<CommonModule>()
);
```

### Key Design Decisions

- One engine (Jint) — no engine abstraction/registry
- TypeScript is a standalone transpiler — not part of the engine
- fetch() is opt-in via `EnableFetch()` for sandboxing
- TaskInterop is default — .NET Task to JS Promise automatically
- DebugMode is opt-in via `EnableDebugMode()`
- Sealed classes for JIT optimization
- Pre-parsed scripts via `JsEngine.Prepare()` for repeated execution

### Built-in JS Globals

**Always-on (safe primitives):** `structuredClone`, `atob`/`btoa`, `performance.now()`, `TextEncoder`/`TextDecoder`. `Type.*` is on whenever `AddDiscriminatorMappings` / `AddTypeAlias` is configured.

**Opt-in via builder flag (4.0+):** `console.*` (`EnableConsole`), `setTimeout`/`setInterval`/`clearTimeout`/`clearInterval` (`EnableTimers`), `NewObject` (`EnableNewObject` + optional `EnableNewObjectAssemblyFallback`), `require` (`EnableRequire`), `fetch` (`EnableFetch`).

**Removed in 4.0:** `exit()` (use IIFE for early-return). See `SECURITY.md` for the threat model.

Jint provides none of these natively — they are all set up by `JsEngine.Initialize`.

## Solution

- Solution file: `src/Cocoar.JsEval.slnx` (XML format)
- Central package management: `Directory.Packages.props`
- Website: VitePress in `website/`
