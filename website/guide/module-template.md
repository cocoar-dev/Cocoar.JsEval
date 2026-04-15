# Template Module

The Template module provides template rendering using [Scriban](https://github.com/scriban/scriban), a fast and powerful template engine.

**Package:** `Cocoar.JsEval.Module.Template`

## Registration

```csharp
services.AddJsEval(b => b.AddModule<TemplateModule>());
```

## Usage

```javascript
import * as template from 'template'

const result = template.Parse(
    "Hello {{ name }}, you have {{ count }} messages!",
    { name: "World", count: 5 }
);
// "Hello World, you have 5 messages!"
```

## Multiple Data Objects

Pass multiple data objects -- they are merged into a single context:

```javascript
const result = template.Parse(
    "{{ user.name }} - {{ config.theme }}",
    { user: { name: "Alice" } },
    { config: { theme: "dark" } }
);
```

## Scriban Syntax

Scriban supports conditionals, loops, and more:

```liquid
{{ for item in items }}
  - {{ item.name }}: {{ item.price | math.format "0.00" }}
{{ end }}
```

See the [Scriban documentation](https://github.com/scriban/scriban/tree/master/doc) for the full template syntax.
