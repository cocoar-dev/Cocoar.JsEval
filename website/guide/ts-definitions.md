# TypeScript Definitions (.d.ts)

`Cocoar.JsEval.TsDefinition` generates TypeScript type definitions (`.d.ts` files) from your C# classes. This enables **IntelliSense in Monaco Editor** when users write scripts in the browser.

## Why?

When users write scripts in your application, they should get autocomplete for the available API:

```
ctx.               →  UserId, Permissions, ManagedCustomerIds, HasPermission()
query.             →  WhereCustomerIn(), WhereResponsible(), ExcludeArchived(), All()
```

`TsDefinitionService` generates these type definitions automatically from your C# classes — no manual `.d.ts` maintenance needed.

## Setup

```bash
dotnet add package Cocoar.JsEval.TsDefinition
```

```csharp
services.AddJsEval(b => b
    .AddModule<CommonModule>()
    .AddModule<HttpModule>()
);
services.AddTsDefinition();
```

## Generating Definitions

### From Registered Modules

`TsDefinitionService` reads all registered modules and generates type definitions for their public API:

```csharp
var definitionService = sp.GetRequiredService<TsDefinitionService>();

// Get .d.ts files for all registered modules
var definitions = definitionService.GetTsDefinitions();
// → { "global.d.ts": "...", "lib.es5.d.ts": "...", ... }

// Get module-specific imports (export declarations)
var imports = definitionService.GetTsImports();
// → { "common.ts": "export function ...", "http.ts": "export function ..." }
```

### From Custom Types (DefinitionBuilder)

For types that aren't exposed as modules (like `AccessContext` or `QueryBuilder`), use `DefinitionBuilder` directly:

```csharp
var builder = new DefinitionBuilder();
builder.AddTypes(typeof(AccessContext), typeof(QueryBuilder));

var definitions = builder.Render();
// → { "AccessContext.d.ts": "...", "QueryBuilder.d.ts": "..." }
```

## Example: Custom Types for a Script Editor

A typical scenario — generating IntelliSense for an admin UI where users write scripts against your domain API:

```csharp
// Your C# types
public class UserContext
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public bool HasRole(string role) => Roles.Contains(role);
}

public class DataFilter
{
    public DataFilter WhereCategory(string category) { /* ... */ return this; }
    public DataFilter WhereCreatedBy(Guid userId) { /* ... */ return this; }
    public DataFilter ExcludeArchived() { /* ... */ return this; }
    public DataFilter All() { /* ... */ return this; }
}
```

```csharp
// Generate .d.ts
var builder = new DefinitionBuilder();
builder.AddTypes(typeof(UserContext), typeof(DataFilter));
var definitions = builder.Render();
```

This generates TypeScript definitions like:

```typescript
declare interface UserContext {
    UserId: string;
    DisplayName: string;
    Roles: string[];
    HasRole(role: string): boolean;
}

declare interface DataFilter {
    WhereCategory(category: string): DataFilter;
    WhereCreatedBy(userId: string): DataFilter;
    ExcludeArchived(): DataFilter;
    All(): DataFilter;
}
```

### Serving to Monaco Editor

Expose the definitions via an API endpoint:

```csharp
app.MapGet("/api/admin/type-definitions", (TsDefinitionService svc) =>
{
    var builder = new DefinitionBuilder();
    builder.AddTypes(typeof(UserContext), typeof(DataFilter));

    var definitions = builder.Render();

    // Add global variable declarations
    definitions["globals.d.ts"] = """
        declare const ctx: UserContext;
        declare const filter: DataFilter;
        """;

    return definitions;
});
```

In the frontend, load these definitions into Monaco:

```typescript
const definitions = await fetch('/api/admin/type-definitions').then(r => r.json());

for (const [filename, content] of Object.entries(definitions)) {
    monaco.languages.typescript.typescriptDefaults.addExtraLib(content, filename);
}
```

## Type Mapping

C# types are automatically mapped to TypeScript equivalents:

| C# Type | TypeScript Type |
|---|---|
| `string`, `char`, `Guid` | `string` |
| `int`, `double`, `float`, `decimal`, `long`, `short`, `byte` | `number` |
| `bool` | `boolean` |
| `DateTime`, `DateTimeOffset` | `Date` |
| `void` | `void` |
| `object` | `any` |
| `Task<T>`, `ValueTask<T>` | `Promise<T>` |
| `Task`, `ValueTask` | `Promise<void>` |
| `byte[]` | `ArrayBuffer` |

## Caching

`TsDefinitionService` caches its output internally. The first call to `GetTsDefinitions()` or `GetTsImports()` does the reflection work; subsequent calls return the cached result. Since type definitions don't change at runtime, this is safe.

## Extension Methods

If your engine uses extension methods (via `AddExtensionMethods<T>()` in the builder), pass the `JsEngineOptions` to `TsDefinitionService` so it can include them in the generated definitions:

```csharp
var service = new TsDefinitionService(moduleRegistry, engineOptions);
```
