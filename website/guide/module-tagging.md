# Module Tagging

Module tagging lets you control which modules are available to specific engine instances. This is useful for multi-tenant scenarios or restricting script capabilities.

## Tagging Modules

Add tags via the `JsModule` attribute:

```csharp
[JsModule("admin")]
public class AdminModule : IJsModule { /* ... */ }

[JsModule("public")]
public class PublicApiModule : IJsModule { /* ... */ }

[JsModule("admin", "internal")]
public class AuditModule : IJsModule { /* ... */ }

// No tags -- always available
public class CommonModule : IJsModule { /* ... */ }
```

## Filtering by Tags

After obtaining an engine instance, specify which tags to allow:

```csharp
var engine = serviceProvider.GetRequiredService<JsEngine>();

// Only load modules tagged "public" (plus untagged modules)
engine.AddTaggedModules("public");

await engine.ExecuteAsync(script);
```

## Behavior

- **Untagged modules** are always available regardless of tag filters
- **Tagged modules** are only available when at least one of their tags matches the filter
- If a script tries to import a module that doesn't match the tag filter, an `InvalidOperationException` is thrown
- Tag matching is case-insensitive
