# Packages

All packages target **.NET 10.0** and are published to [NuGet.org](https://www.nuget.org/profiles/cocoar-dev).

## Core

| Package | Description |
|---------|-------------|
| [`Cocoar.JsEval`](https://www.nuget.org/packages/Cocoar.JsEval) | Core interfaces and contracts (`IJsModule`, `IScriptEngine`, `JsModuleAttribute`, `JsModuleRegistry`) |
| [`Cocoar.JsEval.Engine`](https://www.nuget.org/packages/Cocoar.JsEval.Engine) | `JsEngine`, `IJsEngine`, `JsEvalBuilder`, DI registration (`AddJsEval()`) |

## TypeScript

| Package | Description |
|---------|-------------|
| [`Cocoar.JsEval.TypeScript`](https://www.nuget.org/packages/Cocoar.JsEval.TypeScript) | `TsTranspiler` with embedded TypeScript 6.0 compiler |

## Modules

| Package | Dependency | Description |
|---------|-----------|-------------|
| [`Cocoar.JsEval.Module.Common`](https://www.nuget.org/packages/Cocoar.JsEval.Module.Common) | -- | GUID, Sleep, Random |
| [`Cocoar.JsEval.Module.Http`](https://www.nuget.org/packages/Cocoar.JsEval.Module.Http) | ASP.NET Core, Nito.AsyncEx | Fluent HTTP client |
| [`Cocoar.JsEval.Module.Database`](https://www.nuget.org/packages/Cocoar.JsEval.Module.Database) | Npgsql, SqlKata, Microsoft.Data.SqlClient | SQL Server & PostgreSQL |
| [`Cocoar.JsEval.Module.Smtp`](https://www.nuget.org/packages/Cocoar.JsEval.Module.Smtp) | MailKit | Email sending |
| [`Cocoar.JsEval.Module.AngleSharp`](https://www.nuget.org/packages/Cocoar.JsEval.Module.AngleSharp) | AngleSharp | HTML parsing |
| [`Cocoar.JsEval.Module.Template`](https://www.nuget.org/packages/Cocoar.JsEval.Module.Template) | Scriban | Template rendering |
| [`Cocoar.JsEval.Module.Logging`](https://www.nuget.org/packages/Cocoar.JsEval.Module.Logging) | M.E.Logging.Abstractions | Structured logging |
| [`Cocoar.JsEval.Module.VirtualFileSystem`](https://www.nuget.org/packages/Cocoar.JsEval.Module.VirtualFileSystem) | Zio | Virtual filesystem |

## Utilities

| Package | Description |
|---------|-------------|
| [`Cocoar.JsEval.TsDefinition`](https://www.nuget.org/packages/Cocoar.JsEval.TsDefinition) | TypeScript `.d.ts` generation for modules |
| [`Cocoar.JsEval.Linq`](https://www.nuget.org/packages/Cocoar.JsEval.Linq) | Translates JS arrow functions into real Expression Trees so any `IQueryable<T>` provider (Marten, EF Core, LINQ2DB, …) can convert them to native SQL. Includes property-dependency collector. |
