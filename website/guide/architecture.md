# Architecture

Cocoar.JsEval is built around three concepts: a **JsEngine** for script execution, **Modules** that extend the scripting environment, and a **Registry** that connects them through dependency injection.

## Overview

```mermaid
graph TD
    DI["DI Container"] --> JE["JsEngine"]
    DI --> MR["JsModuleRegistry"]
    DI --> TS["TsTranspiler (optional)"]
    JE --> MR
    MR --> M1["Http Module"]
    MR --> M2["Database Module"]
    MR --> M3["..."]
```

## Core Types

| Type | Purpose |
|------|---------|
| `JsEngine` | Interface for testability. Consumers resolve this from DI. |
| `JsEngine` | Main engine. Executes JavaScript, manages modules, exposes values and functions. |
| `JsEvalBuilder` | Fluent builder for configuring engine options and module registration in one call. |
| `IJsModule` | Marker interface for modules. Modules expose methods/properties to scripts. |
| `JsModuleRegistry` | Registry that tracks and instantiates modules. |
| `JsModuleAttribute` | Attribute for naming and tagging modules. |
| `JsPreparedScript` | A pre-parsed script that can be cached and reused across engine instances. |
| `TsTranspiler` | Standalone TypeScript-to-JavaScript transpiler. |

## JsEngine

`JsEngine` wraps [Jint](https://github.com/sebastienros/jint) 4.8, a .NET JavaScript interpreter (ES2025). It supports:

- Four execution methods:
  - `ExecuteAsync(string)` -- **standard**, full ES modules, import/export, async/await
  - `Evaluate(string)` / `Evaluate(JsPreparedScript)` -- lightweight sync, no module system, ideal for policy evaluation
  - `EvaluateAsync(string)` -- lightweight async, no module system but supports await
- Pre-parsed scripts via `JsEngine.Prepare()` for repeated execution
- Setting and getting values (`SetValue`, `GetValue`, `GetValueAsJson`)
- Function invocation (`GetFunction`, `InvokeFunction`)
- JSON serialization/deserialization (`JsonParse`, `JsonStringify`)
- Module loading via ES imports (in `ExecuteAsync` path)
- Engine reuse -- `Evaluate()`, `EvaluateAsync()`, and `ExecuteAsync()` can be called multiple times on the same instance

## Modules

Modules are plain .NET classes marked with `[JsModule]` that implement `IJsModule`. They receive `IScriptEngine` via constructor injection and expose public methods and properties to scripts.

In JavaScript/TypeScript, modules are available as ES module imports:

```javascript
import * as http from 'http'
import * as common from 'common'
```

## Registration Flow

```mermaid
sequenceDiagram
    participant App
    participant DI as DI Container
    participant Opts as JsEngineOptions
    participant Reg as JsModuleRegistry

    App->>DI: services.AddJsEval(b => b.EnableFetch().AddModule<HttpModule>())
    DI->>Opts: EnableFetch(), etc.
    DI->>Reg: AddModule<HttpModule>()
    App->>DI: GetRequiredService<JsEngine>()
    DI-->>App: JsEngine instance
```

## Module Instantiation

When a script calls `require('modulename')` or `import * from 'modulename'`:

1. The engine asks the `JsModuleRegistry` for the module
2. The registry creates an instance, injecting `IScriptEngine` and any DI services
3. The module's public API becomes available to the script
4. Module instances are cached per engine instance

## Package Structure

JsEval is split into focused packages:

- **Cocoar.JsEval** -- Core interfaces (`IJsModule`, `IScriptEngine`, `JsModuleAttribute`, `JsModuleRegistry`)
- **Cocoar.JsEval.Engine** -- `JsEngine`, `JsEvalBuilder`, DI registration (`AddJsEval()`)
- **Cocoar.JsEval.TypeScript** -- `TsTranspiler`, DI registration (`AddTsTranspiler()`)
- **Cocoar.JsEval.TsDefinition** -- TypeScript `.d.ts` generation for modules
- **Cocoar.JsEval.Module.*** -- 9 built-in modules

See the [Packages](/reference/packages) page for the full listing.
