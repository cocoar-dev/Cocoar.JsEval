# Custom Modules

You can create your own modules to expose application-specific functionality to scripts.

::: warning Modules grant host capabilities
A registered module is an explicit capability granted by the host. Its public
API may intentionally provide database, HTTP, filesystem or even reflection
access. Scripts are therefore only as restricted as the modules made available
to them. This is expected behavior, not a sandbox escape.

Use an allowlist appropriate for each execution context, expose the smallest
useful API, and do not return internal service or repository instances. See
[JSON-only Sandbox](/guide/sandbox#capability-modules) for the sandbox capability
model and its current implementation status.
:::

## Creating a Module

A module is a class that implements `IJsModule`:

```csharp
using Cocoar.JsEval;

[JsModule]
public class MyModule : IJsModule
{
    private readonly IScriptEngine _engine;

    public MyModule(IScriptEngine engine)
    {
        _engine = engine;
    }

    public string Hello(string name) => $"Hello, {name}!";

    public int Add(int a, int b) => a + b;
}
```

## Registration

```csharp
services.AddJsEval(b => b.AddModule<MyModule>());
```

## Usage in Scripts

The module name is derived from the class name with the `Module` suffix removed, in lowercase:

```javascript
import * as my from 'my'

const greeting = my.Hello("World");    // "Hello, World!"
const sum = my.Add(1, 2);             // 3
```

## Custom Module Name

Override the default name using the `JsModule` attribute:

```csharp
[JsModule(Name = "Utils")]
public class MyUtilityModule : IJsModule
{
    // ...
}
```

```javascript
import * as utils from 'utils'
```

## Constructor Injection

Module constructors can receive:
- `IScriptEngine` -- the current engine instance (always available)
- Any service registered in the DI container

```csharp
public class NotificationModule : IJsModule
{
    private readonly IScriptEngine _engine;
    private readonly IEmailService _emailService;

    public NotificationModule(IScriptEngine engine, IEmailService emailService)
    {
        _engine = engine;
        _emailService = emailService;
    }

    public void Send(string to, string message)
    {
        _emailService.Send(to, message);
    }
}
```

## Module Parameter Instances

Provide runtime-specific instances to module constructors:

```csharp
var engine = serviceProvider.GetRequiredService<JsEngine>();

// Register a factory for a specific type
engine.AddModuleParameterInstance(typeof(HttpContext), () => currentHttpContext);
```
