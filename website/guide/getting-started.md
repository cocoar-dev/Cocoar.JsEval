# Getting Started

Cocoar.JsEval lets you execute JavaScript and TypeScript from within your .NET applications. Scripts can access .NET functionality through a modular plugin system.

## Installation

Install the engine package and any modules you need:

```bash
# Engine (required)
dotnet add package Cocoar.JsEval.Engine

# TypeScript transpilation (optional)
dotnet add package Cocoar.JsEval.TypeScript

# Modules (pick what you need)
dotnet add package Cocoar.JsEval.Module.Common
```

## Register with DI

Configure JsEval in your dependency injection container:

```csharp
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Module.Common;

services.AddJsEval(b => b
    .EnableFetch()
    .AddModule<CommonModule>());
```

## Execute a Script

Resolve `IJsEngine` from DI and execute scripts:

```csharp
var engine = serviceProvider.GetRequiredService<IJsEngine>();

// Standard — full ES module support (import/export, async/await)
engine.SetValue("name", "World");
await engine.ExecuteAsync("export const greeting = `Hello, ${name}!`");
var result = engine.GetValue<string>("greeting");
// "Hello, World!"

// Lightweight sync — no module system, for controlled scripts
engine.SetValue("name", "World");
engine.Evaluate("const greeting = `Hello, ${name}!`");
var result2 = engine.GetValue<string>("greeting");
// "Hello, World!"
```

## Use Modules in Scripts

Modules expose .NET functionality to scripts via ES module imports:

```javascript
import * as common from 'common'

const guid = common.Guid.New();
const text = JSON.stringify({ id: guid, timestamp: Date.now() });
```

## TypeScript Support

Use `TsTranspiler` to compile TypeScript to JavaScript before execution:

```csharp
services.AddJsEval();
services.AddTsTranspiler();

// ...
var transpiler = serviceProvider.GetRequiredService<TsTranspiler>();
var engine = serviceProvider.GetRequiredService<IJsEngine>();

var tsScript = @"
const a: number = 1;
const b: number = 2;
export const sum: number = a + b;
";

var jsScript = transpiler.Transpile(tsScript);
await engine.ExecuteAsync(jsScript);

var result = engine.GetValue<int>("sum"); // 3
```

## Prepared Scripts

Pre-parse scripts for repeated execution:

```csharp
var engine = serviceProvider.GetRequiredService<IJsEngine>();

// Parse once
var prepared = JsEngine.Prepare("const x = 1 + 2;");

// Execute many times — skips re-parsing
engine.Evaluate(prepared);
var result = engine.GetValue<int>("x"); // 3
```

`JsPreparedScript` is thread-safe and can be shared across engine instances.

## Next Steps

- Learn about the [Architecture](/guide/architecture) of JsEval
- Explore the [JsEngine](/guide/engine) capabilities
- Browse the [Module catalog](/guide/modules)
