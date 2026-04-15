# JsEngine

`JsEngine` is the core of Cocoar.JsEval. It wraps [Jint](https://github.com/sebastienros/jint) 4.8, a fully compliant ES2025 JavaScript interpreter for .NET.

## Setup

```csharp
services.AddJsEval();
```

Resolve the engine via `IJsEngine` for testability:

```csharp
var engine = serviceProvider.GetRequiredService<IJsEngine>();
```

## Configuration

Configure the engine using the `JsEvalBuilder`:

```csharp
services.AddJsEval(b => b
    .EnableFetch()
    .EnableDebugMode()
    .AllowCurrentDomainAssemblies()
    .AddExtensionMethods<MyExtensions>()
    .AddModule<HttpModule>());
```

### JsEvalBuilder

| Method | Description |
|--------|-------------|
| `EnableFetch()` | Enable the browser-compatible `fetch()` global |
| `EnableDebugMode()` | Enable Jint debug mode (opt-in, not enabled by default) |
| `AllowCurrentDomainAssemblies()` | Allow access to all loaded CLR assemblies |
| `AllowAssemblies(params Assembly[])` | Allow access to specific CLR assemblies |
| `AddExtensionMethods<T>()` | Register extension methods from a type |
| `AddExtensionMethods(params Type[])` | Register extension methods from types |
| `AddModule<T>()` | Register a module for use in scripts |

## Execution Methods

JsEngine provides four execution methods for different use cases:

### ExecuteAsync -- Standard (full-featured)

`ExecuteAsync(string)` is the **default/standard** method. It runs scripts as ES modules with full `import`/`export`, `async`/`await`, and module system support. Use this when you don't know what's in the script.

```csharp
var engine = serviceProvider.GetRequiredService<IJsEngine>();

await engine.ExecuteAsync(@"
import * as common from 'common'

const id = common.Guid.New();
export const result = id;
");

var result = engine.GetValue<string>("result");
```

### Evaluate -- Lightweight sync

`Evaluate(string)` and `Evaluate(JsPreparedScript)` run scripts synchronously without the module system. No `import`/`export`, no `async`/`await`. This is a **conscious opt-in** for a restricted execution mode -- choose it when you know your scripts don't need modules. Ideal for policy evaluation, rule engines, and simple expressions.

```csharp
var engine = serviceProvider.GetRequiredService<IJsEngine>();

engine.SetValue("age", 25);
engine.Evaluate("const allowed = age >= 18;");
var allowed = engine.GetValue<bool>("allowed"); // true
```

### EvaluateAsync -- Lightweight async

`EvaluateAsync(string)` runs scripts asynchronously but without the module system. Use this when your script needs `async`/`await` but doesn't need modules.

```csharp
var engine = serviceProvider.GetRequiredService<IJsEngine>();

engine.SetValue("loadData", new Func<string, Task<string>>(async id => await db.FindAsync(id)));
await engine.EvaluateAsync("const data = await loadData('item-123');");
var data = engine.GetValue<string>("data");
```

### Comparison

| Method | Module System | async | Prepared | Use Case |
|---|:---:|:---:|:---:|---|
| `ExecuteAsync(string)` | Yes | Yes | No | **Standard** -- use when you don't know what's in the script |
| `Evaluate(string)` | No | No | No | Lightweight sync -- when you control the script |
| `Evaluate(JsPreparedScript)` | No | No | Yes | Max performance -- pre-parsed, reusable |
| `EvaluateAsync(string)` | No | Yes | No | Lightweight async -- no modules but needs await |

::: tip
`ExecuteAsync` is the **default/standard** -- it provides the full module system and is always safe. `Evaluate` is NOT the "sync version of ExecuteAsync" -- it's a different, restricted execution mode that you opt into when you know your scripts don't need modules.
:::

## Prepared Scripts

`JsEngine.Prepare(script)` returns a `JsPreparedScript` that can be cached and reused. The script is parsed once, avoiding re-parsing on every execution.

```csharp
// Parse once (thread-safe, shareable across engine instances)
var prepared = JsEngine.Prepare("const x = a + b;");

// Execute many times
var engine = serviceProvider.GetRequiredService<IJsEngine>();
engine.SetValue("a", 10);
engine.SetValue("b", 20);
engine.Evaluate(prepared);
var result = engine.GetValue<int>("x"); // 30
```

::: tip
`JsPreparedScript` is thread-safe and can be stored as a static field or in a cache. Share it across engine instances to avoid redundant parsing.
:::

## Engine Reuse

All execution methods (`Evaluate()`, `EvaluateAsync()`, and `ExecuteAsync()`) can be called multiple times on the same engine instance. Use `SetValue` to update globals between executions -- values are overwritten, not accumulated.

```csharp
var engine = serviceProvider.GetRequiredService<IJsEngine>();

engine.SetValue("x", 1);
engine.Evaluate("const a = x + 1;");
var a = engine.GetValue<int>("a"); // 2

engine.SetValue("x", 10);
engine.Evaluate("const b = x + 1;");
var b = engine.GetValue<int>("b"); // 11
```

## Script Execution (ES Modules)

When using `ExecuteAsync`, scripts are loaded as ES modules. Use `export` to expose values:

```javascript
// Variables
export const result = 42;

// Functions
export function greet(name) {
    return `Hello, ${name}!`;
}
```

## Values

```csharp
// Set values before execution
engine.SetValue("config", new { Timeout = 30, Retries = 3 });

// Get values after execution
var count = engine.GetValue<int>("result");
var json = engine.GetValueAsJson("result");
```

## Functions

Define functions in scripts, then invoke them from .NET:

```csharp
await engine.ExecuteAsync(@"
export function add(a, b) {
    return a + b;
}
");

// Get function metadata
var func = engine.GetFunction("add");
Console.WriteLine(func.Name);           // "add"
Console.WriteLine(func.Parameters.Count); // 2

// Invoke
var result = engine.InvokeFunction("add", 10, 20); // 30
```

## Async / Await

Scripts can use `async`/`await` natively, including awaiting .NET async methods:

```javascript
async function loadData() {
    const response = await fetch('https://api.example.com/data');
    return await response.text();
}

export const data = await loadData();
```

### Awaiting .NET Methods

When .NET methods returning `Task<T>` or `ValueTask<T>` are exposed to the engine, they are automatically converted to JavaScript Promises via TaskInterop:

```csharp
// .NET side: expose an async method
engine.SetValue("loadFromDb", new Func<string, Task<string>>(async (id) =>
{
    return await dbContext.Items.FindAsync(id);
}));
```

```javascript
// Script side: just await it
const item = await loadFromDb('item-123');
```

No configuration needed -- TaskInterop is enabled by default.

## CLR Interop

Create .NET objects from JavaScript using `NewObject`:

```javascript
const dt = NewObject('System.DateTime', [2025, 1, 15]);
```

## Built-in Globals

| Function | Description |
|----------|-------------|
| `fetch(url, options?)` | Browser-compatible HTTP client (opt-in via `EnableFetch()`) |
| `fetchOptions` | Optional fetch configuration (TLS, timeout, proxy) |
| `console.log/info/warn/error/debug` | Logging via `ILogger` from the DI container |
| `setTimeout(fn, ms)` | Schedule a callback after a delay |
| `setInterval(fn, ms)` | Schedule a recurring callback |
| `clearTimeout(id)` | Cancel a timeout |
| `clearInterval(id)` | Cancel an interval |
| `structuredClone(value)` | Deep clone a value |
| `exit()` | Cancels script execution |
| `NewObject(typeName, args)` | Creates a .NET object instance |
| `require(moduleName)` | Loads a registered JsEval module |

### console

`console` methods route to `ILogger` from the DI container:

```javascript
console.log("informational message");
console.info("informational message");
console.warn("warning message");
console.error("error message");
console.debug("debug message");
```

### setTimeout / setInterval

Standard timer functions are available:

```javascript
const id = setTimeout(() => {
    console.log("delayed");
}, 1000);

clearTimeout(id);

const intervalId = setInterval(() => {
    console.log("tick");
}, 500);

clearInterval(intervalId);
```

### structuredClone

Deep clone values, matching the browser API:

```javascript
const original = { nested: { value: 42 } };
const cloned = structuredClone(original);
cloned.nested.value = 99;
// original.nested.value is still 42
```

## Module Imports

Registered modules are available as ES module imports (in `ExecuteAsync` only):

```javascript
import * as common from 'common'
import * as http from 'http'

const response = await fetch('https://api.example.com/data');
const data = JSON.parse(await response.text());
common.Sleep.Seconds(1);
```
