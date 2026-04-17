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

# Run benchmarks
cd src/Benchmarks/JsEval.Benchmarks && dotnet run -c Release -- --filter "*EngineBenchmarks*"

# Pack NuGet packages
dotnet pack src/Cocoar.JsEval.slnx -c Release

# Website dev server
cd website && npm run dev
```

## Architecture Overview

**Cocoar.JsEval** is a JavaScript/TypeScript execution library for .NET 10, built on Jint 4.8 (ES2025).

### Core Packages

- **Cocoar.JsEval** (`src/JsEval/`) — Core interfaces, helpers, JsFunction, IJsModule, JsModuleRegistry
- **Cocoar.JsEval.Engine** (`src/JsEval.Engine/`) — JsEngine, IJsEngine, JsEvalBuilder, fetch(), `CsDateTime`, DI registration
- **Cocoar.JsEval.TypeScript** (`src/JsEval.TypeScript/`) — TsTranspiler with embedded TypeScript 6.0 compiler, engine pooling
- **Cocoar.JsEval.TsDefinition** (`src/JsEval.TsDefinition/`) — .d.ts generation from C# types for IntelliSense
- **Cocoar.JsEval.Linq** (`src/JsEval.Linq/`) — JS arrow function → `Expression<Func<T, TResult>>` translator for any `IQueryable<T>` provider (Marten/EF/LINQ2DB/…), with `linq.*` typed literals, ordering extensions, property-dependency collector, and `ReflectionCache` for hot-loops

### Modules (`src/Modules/`)

8 optional modules: Common, HTTP, Database, SMTP, Template, AngleSharp, Logging, VirtualFileSystem. Module names are **lowercase** in JS imports (e.g., `import * as http from 'http'`).

### Execution Methods

| Method | Module System | async | Use Case |
|---|:---:|:---:|---|
| `ExecuteAsync(string)` | Yes | Yes | **Standard** — use when you don't know what the script contains |
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

`console`, `setTimeout`/`setInterval`, `structuredClone`, `atob`/`btoa`, `performance.now()`, `TextEncoder`/`TextDecoder`, `exit()`, `NewObject()`, `require()`. All provided by JsEval — Jint has none of these natively.

## Solution

- Solution file: `src/Cocoar.JsEval.slnx` (XML format)
- Central package management: `Directory.Packages.props`
- Website: VitePress in `website/`
