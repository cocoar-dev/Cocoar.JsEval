# Other Modules

## Common Module

**Package:** `Cocoar.JsEval.Module.Common`

Provides basic utilities: GUID generation, sleep, and random numbers.

```javascript
import * as common from 'common'

const guid = common.Guid.New();
const empty = common.Guid.Empty;
const parsed = common.Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

common.Sleep.Milliseconds(100);
common.Sleep.Seconds(1);
common.Sleep.Minutes(1);

const num = common.Random.Next(1, 100);
```

## Logging Module

**Package:** `Cocoar.JsEval.Module.Logging`

Structured logging via `Microsoft.Extensions.Logging`.

```javascript
import * as logger from 'logging'

logger.Info(100, "Processing started");
logger.Debug(101, "Step completed");
logger.Warning(102, "Slow response detected");
logger.Error(103, "Operation failed");
logger.Critical(104, "System error");
logger.Trace(105, "Detailed trace info");
```

::: tip
The Logging module uses the `ILogger` from the DI container. Configure logging providers (Console, Serilog, etc.) in your application as usual.
:::

## AngleSharp Module

**Package:** `Cocoar.JsEval.Module.AngleSharp`

HTML parsing and DOM manipulation using [AngleSharp](https://anglesharp.github.io/).

```javascript
import * as html from 'anglesharp'

const doc = html.Parse("<html><body><h1>Title</h1></body></html>");
// Returns an IHtmlDocument for DOM traversal
```

## VirtualFileSystem Module

**Package:** `Cocoar.JsEval.Module.VirtualFileSystem`

Virtual filesystem abstraction using [Zio](https://github.com/xoofx/zio).

```javascript
import * as vfs from 'virtualfilesystem'

const sub = vfs.SubFileSystem("/data");
const agg = vfs.AggregateFileSystem();
const mount = vfs.MountFileSystem();
const path = vfs.BuildUPath("data", "file.txt");
```
