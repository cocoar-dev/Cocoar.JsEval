# Modules Overview

Modules extend the scripting environment with additional capabilities. They are loaded on-demand when a script imports them.

## Available Modules

| Module | Package | Description |
|--------|---------|-------------|
| Common | `Cocoar.JsEval.Module.Common` | GUID generation, Sleep, Random |
| Http | `Cocoar.JsEval.Module.Http` | Fluent HTTP client |
| Database | `Cocoar.JsEval.Module.Database` | SQL Server and PostgreSQL via SqlKata |
| Smtp | `Cocoar.JsEval.Module.Smtp` | Email sending via MailKit |
| AngleSharp | `Cocoar.JsEval.Module.AngleSharp` | HTML parsing and DOM manipulation |
| Template | `Cocoar.JsEval.Module.Template` | Scriban template rendering |
| Logging | `Cocoar.JsEval.Module.Logging` | Microsoft.Extensions.Logging |
| VirtualFileSystem | `Cocoar.JsEval.Module.VirtualFileSystem` | Virtual filesystem via Zio |

## Registration

Register modules during DI setup using the builder:

```csharp
services.AddJsEval(b => b
    .AddModule<HttpModule>()
    .AddModule<DatabaseModule>()
    .AddModule<LoggingModule>());
```

## Usage in JavaScript / TypeScript

Modules are available as ES module imports. The module name is the class name without the `Module` suffix, in lowercase:

```javascript
import * as http from 'http'
import * as common from 'common'
```
