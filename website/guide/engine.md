# JsEngine

`JsEngine` is the core of Cocoar.JsEval. It wraps [Jint](https://github.com/sebastienros/jint) 4.8, a fully compliant ES2025 JavaScript interpreter for .NET.

## Setup

```csharp
services.AddJsEval();
```

Resolve the engine via `JsEngine` for testability:

```csharp
var engine = serviceProvider.GetRequiredService<JsEngine>();
```

::: info DI lifetime: Scoped (since v3.1)
`AddJsEval` registers `JsEngine` as **scoped**. Multiple services resolving `JsEngine` in the same scope (e.g. one HTTP request) share one engine — globals set via `SetValue` are visible across collaborators, and the "Jint is not thread-safe" contract holds by construction. If you need an isolated engine for a specific job, construct one directly with `new JsEngine(...)` (see [v4.1 ctor signature](#wolverine-6-strict-service-location-policy)).

**Migration from v3.0 (transient):** if you relied on each `GetRequiredService<JsEngine>()` producing a fresh instance, either switch to explicit construction or wrap the work in `sp.CreateScope()`.
:::

::: info Wolverine 6 strict service-location policy
Since v4.1, `JsEngine` and `IJsModuleBuilder` are both registered **type-based** (not via lambda factories), so apps running Wolverine 6's strict `ServiceLocationPolicy.NotAllowed` default can inject `JsEngine` into handlers without per-consumer allowlist entries. The single remaining allowlist entry needed is for `IJsModuleBuilder` — it's the intentional locator boundary that activates modules with arbitrary host-resolved constructor parameters:

```csharp
opts.CodeGeneration.AlwaysUseServiceLocationFor<IJsModuleBuilder>();
```

Hosts only ever inject `JsEngine`, never `IJsModuleBuilder`, so the boundary stays clean.

**Migrating from v4.0** — only relevant if you construct `JsEngine` directly (not via `AddJsEval`): the ctor lost its `IServiceProvider` parameter and gained `IJsModuleBuilder`:

```csharp
// Before (4.0):
new JsEngine(serviceProvider, moduleRegistry, options, logger);

// After (4.1):
new JsEngine(moduleRegistry, new JsModuleBuilder(serviceProvider, moduleRegistry), options, logger);
```
:::

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
| `EnableConsole()` | Enable the `console.log/info/warn/error/debug` → `ILogger` bridge |
| `EnableTimers()` | Enable `setTimeout` / `setInterval` / `clearTimeout` / `clearInterval` |
| `EnableNewObject()` | Enable the `NewObject(typeName, args)` JS global (alias-only by default) |
| `EnableNewObjectAssemblyFallback(params Assembly[])` | Allowlist for `NewObject`'s `FindType` fallback. Additive — multiple calls accumulate |
| `EnableRequire()` | Enable the `require(name)` JS global for runtime module loading |
| `EnableDebugMode()` | Enable Jint debug mode (opt-in, not enabled by default) |
| `WithExecutionTimeout(TimeSpan)` | Wall-clock cap per execution (default `10 s`; pass `Timeout.InfiniteTimeSpan` to disable) |
| `WithMaxStatements(int)` | Statement-count cap per execution (default `5 000 000`; pass `0` to disable) |
| `AllowCurrentDomainAssemblies()` | Allow Jint's direct CLR access for all loaded assemblies (orthogonal to `EnableNewObject`) |
| `AllowAssemblies(params Assembly[])` | Allow Jint's direct CLR access for specific assemblies |
| `AddExtensionMethods<T>()` | Register extension methods from a type |
| `AddExtensionMethods(params Type[])` | Register extension methods from types |
| `AddModule<T>()` | Register a module for use in scripts |
| `AddDiscriminatorMappings<TBase>(...)` | Register polymorphic type checks — exposes `Type.Is(a, 'dog')` as a JS global |

::: tip Security defaults (4.0)
Unsafe-by-default JS globals (`NewObject`, `require`, `setTimeout`, `setInterval`, `console`) are off by default. Enable only what your scripts need; `exit()` was removed entirely. See [SECURITY.md](https://github.com/cocoar/cocoar.js-eval/blob/develop/SECURITY.md).
:::

## Execution Methods

JsEngine provides four execution methods for different use cases:

### ExecuteAsync -- Standard (full-featured)

`ExecuteAsync(string)` is the **default/standard** method. It runs scripts as ES modules with full `import`/`export`, `async`/`await`, and module system support. Use this when you don't know what's in the script.

```csharp
var engine = serviceProvider.GetRequiredService<JsEngine>();

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
var engine = serviceProvider.GetRequiredService<JsEngine>();

engine.SetValue("age", 25);
engine.Evaluate("const allowed = age >= 18;");
var allowed = engine.GetValue<bool>("allowed"); // true
```

### EvaluateAsync -- Lightweight async

`EvaluateAsync(string)` runs scripts asynchronously but without the module system. Use this when your script needs `async`/`await` but doesn't need modules.

```csharp
var engine = serviceProvider.GetRequiredService<JsEngine>();

engine.SetValue("loadData", new Func<string, Task<string>>(async id => await db.FindAsync(id)));
await engine.EvaluateAsync("const data = await loadData('item-123');");
var data = engine.GetValue<string>("data");
```

### Comparison

| Method | Module System | async | Prepared | Use Case |
|---|:---:|:---:|:---:|---|
| `ExecuteAsync(string)` | Yes | Yes | No | **Standard** -- use when you don't know what's in the script |
| `ExecuteAsync(JsPreparedModule)` | Yes | Yes | Yes | Max performance for modules -- pre-parsed, ~9× faster on pooled engine |
| `Evaluate(string)` | No | No | No | Lightweight sync -- when you control the script |
| `Evaluate(JsPreparedScript)` | No | No | Yes | Max performance -- pre-parsed, reusable |
| `EvaluateAsync(string)` | No | Yes | No | Lightweight async -- no modules but needs await |

::: tip
`ExecuteAsync` is the **default/standard** -- it provides the full module system and is always safe. `Evaluate` is NOT the "sync version of ExecuteAsync" -- it's a different, restricted execution mode that you opt into when you know your scripts don't need modules.
:::

## Prepared Scripts

Both execution paths have a prepared variant that pre-parses the script once and reuses it across calls.

### Prepare + Evaluate (no modules)

`JsEngine.Prepare(script)` returns a `JsPreparedScript` for the lightweight `Evaluate` path.

```csharp
// Parse once (thread-safe, shareable across engine instances)
var prepared = JsEngine.Prepare("const x = a + b;");

// Execute many times
var engine = serviceProvider.GetRequiredService<JsEngine>();
engine.SetValue("a", 10);
engine.SetValue("b", 20);
engine.Evaluate(prepared);
var result = engine.GetValue<int>("x"); // 30
```

### PrepareModule + ExecuteAsync (with modules)

`JsEngine.PrepareModule(script)` returns a `JsPreparedModule` for the `ExecuteAsync` path. On a pooled engine this is ~9× faster than `ExecuteAsync(string)` because the parse step is skipped and the module cache is seeded immediately.

```csharp
// Parse once — at startup or when the script changes
var preparedModule = JsEngine.PrepareModule(@"
import * as common from 'common'
export function greet(name) { return `Hello, ${name}!`; }
");

// Execute many times
var engine = serviceProvider.GetRequiredService<JsEngine>();
await engine.ExecuteAsync(preparedModule);
var result = engine.InvokeFunction<string>("greet", "World"); // "Hello, World!"
```

::: tip
Both `JsPreparedScript` and `JsPreparedModule` are thread-safe and can be stored as static fields or in a shared cache. Share them across engine instances to avoid redundant parsing.
:::

## Engine Reuse

All execution methods (`Evaluate()`, `EvaluateAsync()`, and `ExecuteAsync()`) can be called multiple times on the same engine instance. Use `SetValue` to update globals between executions -- values are overwritten, not accumulated.

```csharp
var engine = serviceProvider.GetRequiredService<JsEngine>();

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

Create .NET objects from JavaScript using `NewObject` (opt-in — see [Built-in Globals](#built-in-globals) below):

```csharp
services.AddJsEval(b => b
    .EnableNewObject()
    .EnableNewObjectAssemblyFallback(typeof(MyDomainType).Assembly));
```

```javascript
const dt = NewObject('MyDomainType');
```

## Restricting what a script can reach

Passing an object to a script grants more than the object. Every public member is
reachable, and so is every object those members return — a `Customer` with
`Orders` reaches `Order`, which reaches `Tenant`, which reaches whatever the
tenant holds. Nobody decided to expose the last one; it came along, and the
reachable set grows with every property a later refactor adds.

Two independent switches narrow this, and untrusted scripts generally want both.

### `AllowOnly` — the reachable surface

Declares the members a script may use. Anything not declared stops existing for
the script, so the surface is what the list says rather than the transitive
closure of what you passed in.

```csharp
services.AddJsEval(b => b
    .AllowOnly(a => a
        .Member((Customer c) => c.Name)
        .Member((Customer c) => c.Orders)
        .Method((Customer c) => c.Greet(default!))
        .Member((Order o) => o.Total)));
```

```javascript
customer.Name                          // "Alice"
customer.Orders[0].Total               // 99
customer.PasswordHash                  // undefined
customer.Orders[0].Tenant              // undefined — the graph stops here
```

Members are named through expressions, so renaming one is a compile error rather
than a silently narrower sandbox. Enumeration is filtered too: `Object.keys`,
`for..in`, `JSON.stringify` and `Object.entries` report only declared members,
on nested objects as well.

Values the engine never wraps are unaffected, which keeps the list short. A
property returning a `string` still has `startsWith`, and a `DateTime` crosses as
a JS `Date` with its usual methods — neither goes through CLR member resolution.
Use `.Type<T>()` for a value-like framework type whose members you do want
wholesale; do not use it on your own service or entity types, since that is
exactly the transitive reach this list exists to prevent.

A denied member reads as `undefined`, the same as one that never existed. Jint
can raise a `MissingMemberException` instead via
`ConfigureJint(o => o.Interop.ThrowOnUnresolvedMember = true)`, which makes
denials obvious — at the cost of `JSON.stringify` on any wrapped CLR object,
because the same switch fires on its `toJSON` probe.

### `DenyTypes` — types that are never exposed

```csharp
services.AddJsEval(b => b.DenyTypes(typeof(DbContext), typeof(IServiceProvider)));
```

Refuses instances of these types and anything assignable to them. Two checks are
installed: members whose *declared* type is denied disappear, and any value that
turns out to be a denied type *at runtime* is rejected when it would be wrapped.
The second one is what catches a member declared as `object` or an interface,
where the declared type says nothing about what actually comes back — without it
a deny list silently does nothing in exactly those cases.

Treat this as a safety net rather than the boundary. A deny list is only as
complete as its author, whereas `AllowOnly` is closed by construction; denials
earn their keep by catching what someone allows by mistake.

## Sandboxed mode

`Sandboxed()` locks the engine into a hardened configuration and refuses any
later call that would widen it again.

```csharp
services.AddJsEval(b => b
    .Sandboxed()
    .AllowOnly(a => a.Member((Customer c) => c.Name))
    .DenyTypes(typeof(DbContext)));
```

It sets strict mode, disables `eval` and the `Function` constructor, fixes
culture and time zone to invariant/UTC, applies memory, recursion,
execution-stack, array and regex limits, tightens the execution timeout and
statement cap, blocks `GetType()`, reflection and CLR assembly access, removes
`Atomics` / `SharedArrayBuffer`, and freezes the built-in prototypes.

CLR interop stays on — real objects and real method calls are the point of this
surface.

::: warning Sandboxed() does not narrow the object graph
It hardens what a script can do **on its own**; it does not change what a passed
object grants. `holder.Inner.Secret` is still reachable under `Sandboxed()`
alone. Pair it with `AllowOnly` for untrusted scripts — the two axes are
independent.
:::

The latch works in both directions, so the guarantee never depends on the order
the builder happens to be written in:

```csharp
b.Sandboxed().EnableFetch()   // throws: cannot be enabled on a sandboxed engine
b.EnableFetch().Sandboxed()   // throws: cannot be applied after EnableFetch
```

`AllowOnly` and `DenyTypes` remain available afterwards because they narrow
rather than grant.

::: warning What it still does not give you
Isolation between scripts is the engine instance: globals and prototype changes
persist for the lifetime of one `JsEngine`, which is registered per DI scope.
Running scripts from different sources in one scope shares that state — resolve
a separate engine for each.

The memory limit is also enforced between statements, so a single large
allocation (`'x'.repeat(n)`) commits before any limit observes it, and a defect
in Jint itself still reaches the host. For genuinely hostile input, run the
engine in a process with an OS-level memory cap.
:::

## Built-in Globals

Since 4.0 most globals that touch host primitives are **off by default** for security. Enable them explicitly via the corresponding builder flag — see [SECURITY.md](https://github.com/cocoar/cocoar.js-eval/blob/develop/SECURITY.md) for the threat model.

| Function | Default | Enable via |
|----------|---------|------------|
| `btoa` / `atob` | always on | — |
| `performance.now()` | always on | — |
| `TextEncoder` / `TextDecoder` | always on | — |
| `structuredClone(value)` | always on | — |
| `Type.Is(value, discriminator)` | always on (when configured) | `AddDiscriminatorMappings` |
| `fetch(url, options?)` | off | `EnableFetch()` |
| `console.log/info/warn/error/debug` | off | `EnableConsole()` |
| `setTimeout` / `setInterval` / `clearTimeout` / `clearInterval` | off | `EnableTimers()` |
| `NewObject(typeName, args)` | off | `EnableNewObject()` (alias-only) + `EnableNewObjectAssemblyFallback(...)` for unknown types |
| `require(moduleName)` | off | `EnableRequire()` |

**`exit()` was removed in 4.0.** It cancelled the engine's CTS and left the engine permanently dead. Use an IIFE for early-return:

```javascript
(() => {
    if (cond) return earlyResult;
    // …
    return finalResult;
})()
```

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

## Polymorphic Type Checks — `Type.Is` / `Type.IsOneOf`

When scripts work with polymorphic objects, register discriminator mappings to expose `Type.Is(value, discriminator)` and `Type.IsOneOf(value, ['a','b'])` as JS globals.

When the entity has a string field that identifies the type (Marten, flat documents), register the property name once and list the discriminator values:

```csharp
// With Monaco IntelliSense narrowing — view types are never stored in the DB
public class PersonView  : Participant { }
public class CompanyView : Participant { }

services.AddJsEval(b => b
    .AddDiscriminatorMappings<Participant>("ParticipantType",
        ("person",  typeof(PersonView)),
        ("company", typeof(CompanyView))));

// Without Monaco narrowing — Type.Is returns a plain boolean
services.AddJsEval(b => b
    .AddDiscriminatorMappings<Participant>("ParticipantType", "person", "company"));
```

Marten JSONB query generated: `WHERE data->>'ParticipantType' = 'person'`

### Scripts

Scripts call `Type.Is` and `Type.IsOneOf` anywhere — `ExecuteAsync`, `Evaluate`, or LINQ predicates:

```javascript
// Single type check
if (Type.Is(participant, 'person')) {
    console.log(`${participant.Firstname} ${participant.Lastname}`);
}

// Check against multiple types
if (Type.IsOneOf(participant, ['person', 'company'])) {
    console.log(participant.Name);
}

// In a collection filter
const persons = participants.filter(p => Type.Is(p, 'person'));
const either  = participants.filter(p => Type.IsOneOf(p, ['person', 'company']));
```

`Type.Is` and `Type.IsOneOf` return plain `boolean` — they work in JavaScript and TypeScript. The Monaco benefit is the `value is Dog` type-predicate signature generated by `TsDefinitionService` that enables editor narrowing.

### IntelliSense

When `TsDefinitionService` is configured, the `declare const Type` declaration is emitted automatically into `globals.d.ts` — no extra step needed. See [Discriminator Overloads in TsDefinition](./ts-definitions#discriminator-overloads) for Monaco setup.

### LINQ predicates

Both `Type.Is` and `Type.IsOneOf` work as LINQ predicates — the translator intercepts the call and generates the appropriate expression for the active strategy. See [Polymorphic Types in LINQ](./linq#polymorphic-types-discriminator-mapping) for the full LINQ integration.

## Module Imports

Registered modules are available as ES module imports (in `ExecuteAsync` only):

```javascript
import * as common from 'common'
import * as http from 'http'

const response = await fetch('https://api.example.com/data');
const data = JSON.parse(await response.text());
common.Sleep.Seconds(1);
```
