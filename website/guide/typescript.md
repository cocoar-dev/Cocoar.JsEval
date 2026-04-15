# TypeScript Transpilation

`TsTranspiler` compiles TypeScript to JavaScript using an embedded **TypeScript 6.0** compiler targeting **ESNext**. The transpiled output is then executed by `JsEngine`.

JsEval separates concerns: `TsTranspiler` is a standalone service for transpilation, and `JsEngine` handles execution.

## Setup

```csharp
services.AddJsEval();
services.AddTsTranspiler();
```

## Transpilation

Use `TsTranspiler.Transpile()` to convert TypeScript source to JavaScript:

```csharp
var transpiler = serviceProvider.GetRequiredService<TsTranspiler>();
var engine = serviceProvider.GetRequiredService<IJsEngine>();

var tsSource = @"
const greeting: string = 'Hello';
const count: number = 42;
export const result: string = `${greeting} ${count}`;
";

// Transpile TypeScript to JavaScript
var jsSource = transpiler.Transpile(tsSource);

// Execute the transpiled JavaScript
await engine.ExecuteAsync(jsSource);
var result = engine.GetValue<string>("result"); // "Hello 42"
```

::: tip
`TsTranspiler` is registered as a singleton. The embedded TypeScript compiler is loaded once and reused across all transpilation calls.
:::

## Supported TypeScript Features

The embedded TypeScript 6.0 compiler supports all standard TypeScript features:

| Feature | Example |
|---------|---------|
| Type annotations | `const x: number = 42` |
| Interfaces | `interface User { name: string }` |
| Enums (numeric & string) | `enum Color { Red, Green }` |
| Generics | `function identity<T>(v: T): T` |
| Type aliases & unions | `type Id = string \| number` |
| Optional parameters | `function f(x?: string)` |
| Default parameters | `function f(x: number = 0)` |
| Rest parameters | `function f(...args: number[])` |
| Classes with access modifiers | `class Foo { private x: number }` |
| Type assertions | `value as string` |
| Nullish coalescing | `x ?? defaultValue` |
| Optional chaining | `obj?.prop?.method()` |
| Destructuring with types | `const { x, y }: Point = point` |
| Template literals | `` `Hello ${name}` `` |
| Spread operator | `[...arr1, ...arr2]` |
| Arrow functions | `const f = (x: number): number => x * 2` |

All type-level constructs (interfaces, type aliases, generics, annotations) are erased during transpilation. Runtime code (enums, classes, functions) is transpiled to ESNext JavaScript -- modern features like `??`, `?.`, and class fields remain as-is without polyfills, since Jint 4.8 supports up to ES2025.

## CLR Constructor Rewriting

The transpiler automatically rewrites `new TypeName(...)` expressions to `NewObject('TypeName', [...])` for CLR interop:

```typescript
// TypeScript source:
const dt = new System.DateTime(2025, 1, 15);

// Becomes:
const dt = NewObject('System.DateTime', [2025, 1, 15]);
```

To opt out for a specific line, append `//ignore`:

```typescript
const calc = new Calculator(10); //ignore
```

## Module Usage

Same as with `JsEngine` -- modules are available as ES imports:

```typescript
import * as logger from 'logging'

logger.Info(42, 'Processing complete');
```
