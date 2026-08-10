# API Overview

## Core Types

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

A module is the capability channel: `AddModule<T>` grants something the script
may `import`, and each public method is exported as a JavaScript function.
`ExecuteAsync` runs the script as an ES module so `import` resolves, and a module
method returning a `Task` is awaited before its result crosses back.

Return DTOs. A returned object is wrapped, not copied, so whatever it reaches is
reachable from the script unless [`AllowOnly`](#interop-restrictions) narrows it
— returning an internal service or repository hands out its object graph.

A parameter declared as `JsValue` receives the script's value unconverted, which
is how a module accepts an arrow function it means to [translate](/guide/linq)
rather than run. Every other parameter type is marshalled to its CLR equivalent.
An exception thrown by a module keeps its own message when it surfaces in the
script.

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
