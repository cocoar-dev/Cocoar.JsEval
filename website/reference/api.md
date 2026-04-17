# API Overview

## Core Types

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
