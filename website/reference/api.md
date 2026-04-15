# API Overview

## Core Types

### IJsEngine

Interface for testability. Consumers resolve `IJsEngine` from DI instead of the concrete `JsEngine`.

```csharp
public interface IJsEngine : IScriptEngine, IDisposable, IAsyncDisposable
{
    // Values
    void SetValue(string name, object value);
    T? GetValue<T>(string name);
    object GetValue(string name);
    string GetValueAsJson(string name);

    // Execution
    object? Evaluate(string script);
    object? Evaluate(JsPreparedScript preparedScript);
    Task<object?> EvaluateAsync(string script);
    Task<object?> ExecuteAsync(string script);
    void Stop();

    // Functions
    JsFunction? GetFunction(string name);
    object InvokeFunction(string name, params object[] args);

    // Modules
    void AddModuleParameterInstance(Type type, Func<object> factory);
    void AddTaggedModules(params string[] tags);
    T? GetModuleState<T>();
}
```

### JsEngine

Main engine for JavaScript execution. Implements `IJsEngine`.

```csharp
public class JsEngine : IJsEngine
{
    // Static
    static JsPreparedScript Prepare(string script);

    // JSON (IScriptEngine)
    object? JsonParse(string? json);
    string JsonStringify(object? value);
    object? ConvertToDefaultObject(object? value);
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
