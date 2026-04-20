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

// Get .d.ts files for all registered modules, plus global.d.ts (fetch, require, …)
var definitions = definitionService.GetTsDefinitions();
// → { "global.d.ts": "...", "System.d.ts": "...", "<Module>.d.ts": "...", ... }

// Get module-specific imports (export declarations)
var imports = definitionService.GetTsImports();
// → { "common.ts": "export function ...", "http.ts": "export function ..." }
```

::: tip Standard-Library types
This package generates `.d.ts` from **your** C# types — it does not ship the TypeScript standard library (`lib.es5.d.ts`, `lib.dom.d.ts`, …). Monaco's own TypeScript language service loads version-matched libs automatically; if you need a specific newer set, embed the TS 6.0.2 ES-only libs from `Cocoar.JsEval.TypeScript.V8.EmbeddedResources.LibFiles`.
:::

## Short names — aliases + namespace mapping

`.d.ts` from reflection tends to emit types *fully qualified* — `TimeToDo.Infrastructure.Persistence.Marten.Projections.Customers.CustomerView` — and Monaco's hover popup shows that 80-character string. Since v3.1.4 you can register **short names** that are simultaneously:

- Emitted at **root scope** in the `.d.ts` (`declare class CustomerView { … }` without namespace wrapper)
- Resolvable by `NewObject("CustomerView")` at runtime — the same name works on both sides
- Used in cross-references between other rendered types (`Person: PersonData` instead of `Person: TimeToDo.…Principals.PersonData`)

### Per-type alias

```csharp
services.AddJsEval(js => js
    .AddTypeAlias<PrincipalDirectory>()             // uses typeof(T).Name → "PrincipalDirectory"
    .AddTypeAlias<CustomerView>("CustomerView")     // explicit name
);
```

### Bulk namespace mapping

One rule for a whole projection tree:

```csharp
services.AddJsEval(js => js
    .MapNamespace("TimeToDo.Infrastructure.Persistence.Marten.Projections", "")
    .MapNamespace("TimeToDo.Domain.Identity", "")
);
```

**Target-prefix semantics:**
- **Empty target** (`MapNamespace("X.Y.Z", "")`) — **full flatten**: every type under the source prefix lands at root scope, regardless of how deeply nested the original namespace was. A type in `X.Y.Z.Sub.Inner` emits at root, not under `Sub.Inner`.
- **Non-empty target** (`MapNamespace("X.Y.Z", "Legacy")`) — **strip + prepend**: sub-namespace structure is preserved. `X.Y.Z.Sub.Inner.Foo` becomes `Legacy.Sub.Inner.Foo`.

`System.*` types are excluded from mapping by default — they stay fully qualified so Monaco still recognizes `Guid`, `DateTime`, etc.

### Collision detection

Two distinct types resolving to the same final name throw at render time with actionable next steps:

```
Type alias/mapping collision — two or more distinct types resolve to the same short name:
  'CustomerView' at root scope:
    - TimeToDo.Projections.Customers.CustomerView
    - TimeToDo.Reports.CustomerView

Resolve by one of:
  - Adding an explicit AddTypeAlias(typeof(X), "UniqueName") on one of them
  - Narrowing MapNamespace(...) to only one source prefix
  - Using a non-empty target prefix in MapNamespace to disambiguate
  - Excluding one of the types from the builder
```

Natural same-name collisions that *no rule touched* (e.g. `Span<T>.Enumerator` and `ReadOnlySpan<T>.Enumerator` — both nested `Enumerator$1` under `System`) still emit as-is; TypeScript merges matching `interface` declarations automatically.

### Standalone (non-DI) use

```csharp
var builder = new DefinitionBuilder()
    .MapNamespace("TimeToDo.Projections", "")
    .AddType(typeof(CustomerView), alias: "CustomerView");
var defs = builder.Render();
```

The standalone path covers `.d.ts` generation only. For the `NewObject`-runtime integration, use `JsEvalBuilder.AddTypeAlias` / `MapNamespace` — those flow through `JsEngineOptions` to both layers.

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
