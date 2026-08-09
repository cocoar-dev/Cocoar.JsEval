# API Overview

## Core Types

### JsSandbox

JSON-only execution surface for untrusted rules. Each call uses a fresh engine
and exposes no CLR objects, modules, host functions, or underlying engine.

```csharp
public sealed class JsSandbox
{
    void SetValue(string name, object? value);
    T? GetValue<T>(string name);
    string GetValueAsJson(string name);
    void Execute(string script);

    void AddModule(string name, IJsModule module);
    Task ExecuteAsync(string script, CancellationToken cancellationToken = default);
}
```

`SetValue` serializes arbitrary named values to JSON. `Execute` loads those
values into a fresh engine and serializes them back afterward; `GetValue<T>`
returns a newly deserialized copy. Resource limits are configured through
`JsSandboxOptions`.

A value name must be a valid JavaScript identifier and must not be a reserved
word or one of the non-writable globals `undefined`, `NaN`, `Infinity` and
`globalThis`; those throw `ArgumentException`.

Execution failures — script errors, timeouts, statement, memory, recursion,
regex, nesting-depth and output-size limits, and non-serializable results —
all surface as `JsSandboxException`, with the originating exception kept as
`InnerException`. A failed `Execute` leaves every value at its previous
content. Host misuse (invalid name, oversized input or script) stays
`ArgumentException`.

`AddModule` grants a capability the script may `import`. The module instance
never enters the engine: each public method is exported as a plain JavaScript
function whose arguments and return value cross as JSON, so CLR interop stays
off and a script cannot climb from a returned value into the host object graph.
Return DTOs — a returned object's public properties are serialized into the
script's reach. A parameter declared as `JsValue` is the one exception and
receives the script's value unconverted, which is how a module accepts an arrow
function it means to translate rather than run. `ExecuteAsync` runs the script as
an ES module so `import` resolves; a module method returning a `Task` is awaited
before its result crosses back.

### Interop restrictions

```csharp
services.AddJsEval(b => b
    .Sandboxed()                                  // hardened runtime, latched
    .AllowOnly(a => a.Member((Customer c) => c.Name))
    .DenyTypes(typeof(DbContext))
    .ConfigureJint(o => { /* raw Jint options */ })
);
```

`Sandboxed()` hardens what a script can do on its own and rejects any later
capability grant, in either order. `AllowOnly` declares the reachable members —
everything else stops existing for the script. `DenyTypes` refuses types by
declared *and* runtime type. `ConfigureJint` reaches Jint options that only apply
at construction time, which `RegisterEngineConfigurator` cannot. See the
[JsEngine guide](/guide/engine#restricting-what-a-script-can-reach).

### JsEngine

Main engine for JavaScript execution. Sealed class, resolved from DI.

```csharp
public sealed class JsEngine : IScriptEngine, IDisposable, IAsyncDisposable
{
    // Underlying Jint engine for advanced interop
    Jint.Engine UnderlyingEngine { get; }

    // Values
    void SetValue(string name, object value);
    T? GetValue<T>(string name);
    string GetValueAsJson(string name);

    // Execution (statement semantics — void return)
    void Evaluate(string script);
    void Evaluate(JsPreparedScript prepared);
    Task EvaluateAsync(string script);
    Task ExecuteAsync(string script);

    // Expression-semantics evaluation (returns the resulting JsValue)
    Jint.Native.JsValue EvaluateExpression(string script);

    // Functions
    JsFunction? GetFunction(string name);
    object InvokeFunction(string name, params object[] args);
    void Stop();

    // Static
    static JsPreparedScript Prepare(string script);

    // JSON (IScriptEngine)
    object? JsonParse(string? json);
    string JsonStringify(object? value);
}
```

### JsPreparedScript

A pre-parsed script that avoids re-parsing on every execution. Thread-safe and shareable across engine instances.

```csharp
public sealed class JsPreparedScript
{
    // Created via JsEngine.Prepare(script)
}
```

### IScriptEngine

Minimal interface for script engine capabilities needed by modules.

```csharp
public interface IScriptEngine
{
    object? JsonParse(string? json);
    string JsonStringify(object? value);
}
```

### JsEvalBuilder

Fluent builder that unifies engine options and module registration in a single call.

```csharp
public class JsEvalBuilder
{
    JsEvalBuilder Sandboxed();
    JsEvalBuilder AllowOnly(Action<JsInteropAllowlist> build);
    JsEvalBuilder DenyTypes(params Type[] types);
    JsEvalBuilder ConfigureJint(Action<Jint.Options> configure);
    JsEvalBuilder EnableFetch();
    JsEvalBuilder EnableDebugMode();
    JsEvalBuilder AllowCurrentDomainAssemblies();
    JsEvalBuilder AllowAssemblies(params Assembly[] assemblies);
    JsEvalBuilder AddExtensionMethods<T>();
    JsEvalBuilder AddExtensionMethods(params Type[] types);
    JsEvalBuilder AddModule<T>() where T : IJsModule;
}
```

### IJsModule

Marker interface for script modules.

```csharp
public interface IJsModule { }
```

### JsModuleAttribute

Attribute for naming and tagging modules.

```csharp
public class JsModuleAttribute : Attribute
{
    public JsModuleAttribute(params string[] tags);
    public string? Name { get; set; }
    public string[] Tags { get; set; }
}
```

### JsModuleRegistry

Registry that tracks and instantiates modules. Modules are registered via the builder (`AddModule<T>()`) rather than directly.

```csharp
public sealed class JsModuleRegistry : IJsModuleRegistry
{
    IEnumerable<IJsModuleDefinition> GetRegisteredModuleDefinitions();
}
```

### TsTranspiler

Standalone TypeScript-to-JavaScript transpiler.

```csharp
public class TsTranspiler
{
    string Transpile(string sourceCode);
}
```

## DI Registration

```csharp
// Default registration
services.AddJsEval();

// With builder configuration
services.AddJsEval(b => b.EnableFetch());

// With engine options and modules
services.AddJsEval(b => b
    .EnableFetch()
    .AddModule<HttpModule>());

// TypeScript transpiler (separate registration)
services.AddTsTranspiler();
```

`JsEngine` is registered with **scoped** lifetime (since v3.1) — one engine per DI scope, shared across every service that injects it. Jint is not thread-safe, and collaborators in the same scope need a consistent view of globals set via `SetValue`.
