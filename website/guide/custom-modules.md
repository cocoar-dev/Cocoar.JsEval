# Custom Modules

You can create your own modules to expose application-specific functionality to scripts.

::: warning Modules grant host capabilities
A registered module is an explicit capability granted by the host. Its public
API may intentionally provide database, HTTP, filesystem or even reflection
access. Scripts are therefore only as restricted as the modules made available
to them. This is expected behavior, not a sandbox escape.

Use an allowlist appropriate for each execution context, expose the smallest
useful API, and do not return internal service or repository instances — a
returned object is wrapped, not copied, so the script reaches whatever it
reaches. For untrusted scripts, combine the module with
[`Sandboxed()` and `AllowOnly`](/guide/engine#restricting-what-a-script-can-reach),
which decide what the returned object may expose.
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

## How arguments and results cross

Each public method of a module is exported as a JavaScript function. Arguments are
marshalled to the parameter's CLR type, and a method returning a `Task` is awaited
before its result crosses back.

**Return DTOs.** A returned object is *wrapped*, not copied — the script reaches
whatever that object reaches. Handing back an internal service or repository hands
out its object graph. For untrusted scripts, combine the module with
[`AllowOnly`](/guide/engine#restricting-what-a-script-can-reach), which decides
which members of a returned object exist for the script.

**A `JsValue` parameter receives the script's value unconverted.** This is the one
exception to argument marshalling, and it is how a module accepts an arrow function
it means to inspect rather than run — a rule it will
[translate into an expression tree](/guide/linq#reaching-a-module-from-inside-a-rule):

```csharp
public sealed class RulesModule : IJsModule
{
    // Without the JsValue parameter type the arrow would arrive as a delegate
    // and its AST -- the whole point -- would be gone.
    public List<UserDto> Find(JsValue rule) { /* translate `rule`, then query */ }
}
```

An exception thrown inside a module keeps its own message when it surfaces in the
script, so a module's validation reads as the reason the call was refused.
