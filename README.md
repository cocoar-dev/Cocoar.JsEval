# Cocoar.JsEval

JavaScript/TypeScript execution library for .NET, built on [Jint](https://github.com/sebastienros/jint).

[![NuGet](https://img.shields.io/nuget/v/Cocoar.JsEval.svg)](https://www.nuget.org/packages/Cocoar.JsEval)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE.txt)

## Features

- JavaScript execution via Jint (ES2025 support)
- Four execution methods: `ExecuteAsync()` (standard), `Evaluate()`, `Evaluate(prepared)`, `EvaluateAsync()`
- Pre-parsed scripts (`Prepare()`) for maximum throughput
- TypeScript 6.0 transpilation with embedded compiler
- `fetch()` API with opt-in sandboxing
- Automatic .NET `Task` → JS `Promise` interop
- `console.log/warn/error/debug` via `ILogger`
- `setTimeout`/`setInterval` support
- Extensible module system (HTTP, Database, SMTP, Templates, and more)
- `.d.ts` generation for IntelliSense support
- `IJsEngine` interface for testability and mocking
- Built for .NET 10

## Quick Start

```bash
dotnet add package Cocoar.JsEval.Engine
```

### Basic JavaScript Execution

```csharp
services.AddJsEval();
```

```csharp
var engine = sp.GetRequiredService<IJsEngine>();
engine.SetValue("name", "World");
engine.Evaluate("var greeting = 'Hello, ' + name + '!';");
var result = engine.GetValue<string>("greeting"); // "Hello, World!"
```

### With ES Modules

```csharp
services.AddJsEval(b => b
    .AddModule<CommonModule>()
    .AddModule<HttpModule>()
);
```

```csharp
var engine = sp.GetRequiredService<IJsEngine>();
await engine.ExecuteAsync(@"
    import * as common from 'common'
    export const id = common.Guid.New();
");
```

### Pre-Parsed Scripts (for repeated execution)

```csharp
// Parse once (thread-safe, cacheable)
var prepared = JsEngine.Prepare("query.WhereResponsible(ctx.UserId);");

// Execute many times — no re-parsing
engine.SetValue("ctx", accessContext);
engine.SetValue("query", queryBuilder);
engine.Evaluate(prepared);
```

### TypeScript Transpilation

```bash
dotnet add package Cocoar.JsEval.TypeScript
```

```csharp
services.AddTsTranspiler();
```

```csharp
var transpiler = sp.GetRequiredService<TsTranspiler>();
var js = transpiler.Transpile(tsCode);  // transpile once
await engine.ExecuteAsync(js);          // execute
```

### fetch()

```csharp
services.AddJsEval(b => b.EnableFetch());
```

```javascript
const response = await fetch('https://api.example.com/data');
const body = await response.text();
console.log(response.status, response.ok);
```

### .NET async → JS Promise (automatic)

```csharp
engine.SetValue("loadData", new Func<string, Task<string>>(async id => {
    return await db.FindAsync(id);
}));
```

```javascript
const data = await loadData('item-123'); // .NET Task becomes a Promise
```

## Execution Methods

| Method | Module System | async | Prepared | Use Case |
|---|:---:|:---:|:---:|---|
| `ExecuteAsync(string)` | Yes | Yes | No | **Standard** -- use when you don't know what's in the script |
| `Evaluate(string)` | No | No | No | Lightweight sync -- when you control the script |
| `Evaluate(JsPreparedScript)` | No | No | Yes | Max performance -- pre-parsed, reusable |
| `EvaluateAsync(string)` | No | Yes | No | Lightweight async -- no modules but needs await |

`ExecuteAsync` is the **default/standard** method -- it provides the full module system and is always safe. `Evaluate` is a **conscious opt-in** for a restricted execution mode -- choose it when you know your scripts don't need modules.

## Packages

| Package | Description |
|---|---|
| `Cocoar.JsEval` | Core: interfaces, helpers, JsFunction |
| `Cocoar.JsEval.Engine` | JsEngine + fetch() + DI registration |
| `Cocoar.JsEval.TypeScript` | TypeScript 6.0 transpiler |
| `Cocoar.JsEval.TsDefinition` | .d.ts generation for IntelliSense |
| `Cocoar.JsEval.Expressions` | Expression Tree helpers for LINQ-compatible filters |
| `Cocoar.JsEval.Module.Common` | Guid, Sleep, Random |
| `Cocoar.JsEval.Module.Http` | Fluent HTTP client |
| `Cocoar.JsEval.Module.Database` | SQL Server + PostgreSQL |
| `Cocoar.JsEval.Module.Smtp` | Email via MailKit |
| `Cocoar.JsEval.Module.AngleSharp` | HTML parsing |
| `Cocoar.JsEval.Module.Template` | Scriban templates |
| `Cocoar.JsEval.Module.Logging` | Microsoft.Extensions.Logging |
| `Cocoar.JsEval.Module.VirtualFileSystem` | Zio VFS |

## License

[Apache-2.0](LICENSE.txt) — COCOAR e.U.
