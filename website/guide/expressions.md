# Expression Helpers

`Cocoar.JsEval.Expressions` provides building blocks for constructing LINQ-compatible Expression Trees. These are particularly useful when your scripts drive data filtering — the script defines **what** to filter, and the Expression Tree translates it to SQL via your ORM.

```bash
dotnet add package Cocoar.JsEval.Expressions
```

::: info No ORM dependency
This package has zero external dependencies. It works with Marten, EF Core, or any LINQ provider.
:::

## Why?

When you build Expression Trees dynamically (e.g., from script-driven logic), you run into problems that don't exist with normal C# lambdas:

1. **Enum comparison** — C# compiler inserts `Convert(enum, Int32)`, which breaks ORMs that store enums as strings
2. **`Contains`/`IN` operator** — `Expression.Constant(list)` creates a naked constant that LINQ providers can't translate to SQL
3. **Combining expressions** — Merging multiple `Expression<Func<T, bool>>` with AND/OR requires parameter rebinding

These helpers solve all three.

## Enum Comparison

### The Problem

When you write `x => x.Status == MyEnum.Active` in C#, the compiler generates:

```
Convert(x.Status, Int32) == Convert(Active, Int32)
```

This translates to SQL like `CAST(data->>'Status' AS integer) = 1`. But if your database stores enums as strings (`"Active"`), the query returns zero results.

### Three Strategies

Choose the strategy that matches your database configuration:

```csharp
using Cocoar.JsEval.Expressions;

// 1. Native enum — no Convert, let the ORM decide (safest default)
var expr = EnumExpressionHelper.Equals<TodoView, TodoStatus>("Status", TodoStatus.Active);
// Produces: x => x.Status == TodoStatus.Active

// 2. Explicit string comparison (for string-stored enums)
var expr = EnumExpressionHelper.EqualsAsString<TodoView>("Status", "Active");
// Produces: x => x.Status.ToString() == "Active"

// 3. Explicit integer comparison (for int-stored enums)
var expr = EnumExpressionHelper.EqualsAsInt<TodoView, TodoStatus>("Status", TodoStatus.Active);
// Produces: x => (int)x.Status == 1
```

### Parsing from String Input

When the enum value comes from user input or a script (as a string):

```csharp
// Parse string to enum, compare as native enum type
var expr = EnumExpressionHelper.Equals<TodoView>("Status", typeof(TodoStatus), "Active");

// Parse string, compare as integer
var expr = EnumExpressionHelper.EqualsAsInt<TodoView>("Status", typeof(TodoStatus), "active"); // case-insensitive
```

### When to Use Which

| Strategy | Use When | Example ORM Config |
|---|---|---|
| `Equals` | You trust the ORM to handle it | Default / unknown |
| `EqualsAsString` | DB stores enums as `"Active"` | Marten `EnumStorage.AsString`, EF Core `HasConversion<string>()` |
| `EqualsAsInt` | DB stores enums as `1` | EF Core default, classic ADO.NET |

## Contains / IN Operator

### The Problem

```csharp
var customerIds = new List<Guid> { id1, id2, id3 };

// This FAILS with most LINQ providers:
Expression.Constant(customerIds)  // → naked ConstantExpression
// SQL translation fails — provider doesn't know how to parameterize a raw constant
```

LINQ providers expect the collection to come from a **closure** (a `MemberExpression` on a captured variable), not a naked `ConstantExpression`. That's how the C# compiler generates `list.Contains(x.Id)` — it captures `list` in a closure class.

### The Fix: ListHolder

`ListHolder<T>` simulates the closure pattern:

```csharp
var customerIds = new List<Guid> { id1, id2, id3 };

// Option 1: Use the helper directly
var expr = ExpressionHelper.Contains<TodoView, Guid>("CustomerId", customerIds);
// Produces: x => holder.Values.Contains(x.CustomerId)
// Translates to: WHERE customer_id IN ('...', '...', '...')

// Option 2: Use ListHolder in your own expression tree
var holder = ListHolder.Create(customerIds);
var holderExpr = holder.AsExpression();  // MemberExpression, not ConstantExpression
```

## Nested Property Paths

All helpers support dotted property paths for navigating nested objects:

```csharp
// Simple property
ExpressionHelper.Equal<TodoView, string>("Title", "Bug Fix");
// → x => x.Title == "Bug Fix"

// Nested property
ExpressionHelper.Equal<TodoView, string>("Customer.Name", "Acme");
// → x => x.Customer.Name == "Acme"

// Deep nesting works too
ExpressionHelper.StartsWith<TodoView>("Customer.Address.City", "Vie");
```

Use `PropertyPath` directly when building custom expressions:

```csharp
var param = Expression.Parameter(typeof(TodoView), "x");
var property = PropertyPath.Resolve(param, "Customer.Name");
// → MemberExpression: x.Customer.Name

var type = PropertyPath.GetPropertyType(typeof(TodoView), "Customer.Name");
// → typeof(string)
```

## Comparison Operators

```csharp
// Equality
ExpressionHelper.Equal<TodoView, bool>("IsArchived", false);
ExpressionHelper.NotEqual<TodoView, TodoStatus>("Status", TodoStatus.Archived);

// String operations
ExpressionHelper.StartsWith<TodoView>("Title", "Bug:");
ExpressionHelper.StringContains<TodoView>("Description", "urgent");
ExpressionHelper.EndsWith<TodoView>("Title", "Fix");

// Numeric / Date comparison
ExpressionHelper.GreaterThan<TodoView, DateTime>("DueDate", DateTime.Today);
ExpressionHelper.GreaterThanOrEqual<TodoView, int>("Priority", 3);
ExpressionHelper.LessThan<TodoView, DateTime>("CreatedAt", cutoffDate);
ExpressionHelper.LessThanOrEqual<TodoView, int>("Priority", 5);

// Null checks
ExpressionHelper.IsNull<TodoView>("Customer");
ExpressionHelper.IsNotNull<TodoView>("Customer.Name");
```

## Collection Navigation (Any)

For querying collection properties — e.g., "find todos where any responsible matches":

```csharp
// x => x.Responsibles.Any(r => r.Id == userId)
var expr = ExpressionHelper.Any<TodoView, ResponsibleView, Guid>(
    "Responsibles", "Id", userId);

// With custom predicate
Expression<Func<ResponsibleView, bool>> predicate = r => r.Name.StartsWith("A");
var expr = ExpressionHelper.Any<TodoView, ResponsibleView>(
    "Responsibles", predicate);

// Check if collection has any items
var expr = ExpressionHelper.HasAny<TodoView, ResponsibleView>("Responsibles");
// → x => x.Responsibles.Any()
```

## OrderBy

Build dynamic sort expressions from property names (including nested paths):

```csharp
// Untyped — useful for dynamic sorting
LambdaExpression selector = ExpressionHelper.OrderBy<TodoView>("DueDate");
LambdaExpression selector = ExpressionHelper.OrderBy<TodoView>("Customer.Name");

// Typed
Expression<Func<TodoView, DateTime>> selector = ExpressionHelper.OrderBy<TodoView, DateTime>("DueDate");
```

## Combining Expressions

```csharp
var notArchived = ExpressionHelper.Equal<TodoView, bool>("IsArchived", false);
var isUrgent = ExpressionHelper.StringContains<TodoView>("Title", "urgent");

// AND — both must match
var combined = ExpressionHelper.And(notArchived, isUrgent);

// OR — either matches
var either = ExpressionHelper.Or(notArchived, isUrgent);

// NOT — negate
var archived = ExpressionHelper.Not(notArchived);
```

Parameter rebinding is handled automatically — you can combine expressions that were built with different `ParameterExpression` instances.

## Expression Rewriter

If you receive an Expression Tree from external code (e.g., C# compiler-generated lambda) that has the `Convert(enum, Int32)` problem, you can fix it after the fact:

```csharp
// Original expression with Convert nodes (from C# compiler)
Expression<Func<TodoView, bool>> original = x => x.Status == status;
// → Convert(x.Status, Int32) == Convert(value, Int32)

// Strip the Convert nodes
var fixed = ExpressionRewriter.RewriteEnumConversions(original);
// → x.Status == value (native enum comparison)
```

## FilterBuilder

`FilterBuilder<T>` is a fluent builder that collects filter expressions and combines them. It wraps all the helpers above into a chainable API:

```csharp
var filter = new FilterBuilder<TodoView>()
    .Where("IsArchived", false)
    .WhereIf(customerIds.Count > 0, b => b.Contains("CustomerId", customerIds))
    .WhereAny<ResponsibleView, Guid>("Responsibles", "Id", userId)
    .WhereGreaterThan("DueDate", DateTime.Today.AddDays(-90))
    .Build();

var results = await session.Query<TodoView>().Where(filter).ToListAsync();
```

### Available Methods

| Method | Description |
|---|---|
| `Where<TValue>(path, value)` | Equality: `x.Prop == value` |
| `WhereNot<TValue>(path, value)` | Not equal: `x.Prop != value` |
| `WhereIf(condition, configure)` | Conditional — only adds filter if condition is true |
| `WhereIf(condition, expression)` | Conditional with raw expression |
| `Contains<TValue>(path, values)` | IN operator with closure pattern |
| `WhereStartsWith(path, value)` | String starts with |
| `WhereContains(path, value)` | String contains |
| `WhereEndsWith(path, value)` | String ends with |
| `WhereGreaterThan<TValue>(path, value)` | Greater than |
| `WhereGreaterThanOrEqual<TValue>(path, value)` | Greater than or equal |
| `WhereLessThan<TValue>(path, value)` | Less than |
| `WhereLessThanOrEqual<TValue>(path, value)` | Less than or equal |
| `WhereNull(path)` | Is null |
| `WhereNotNull(path)` | Is not null |
| `WhereAny<TItem, TValue>(collection, itemProp, value)` | Collection any |
| `WhereAny<TItem>(collection, predicate)` | Collection any with custom predicate |
| `WhereEnum<TEnum>(path, value)` | Enum comparison (native) |
| `WhereEnumAsString(path, value)` | Enum comparison (as string) |
| `Where(expression)` | Raw expression |

### Build Modes

```csharp
// AND — all filters must match (default)
var filter = builder.Build();

// OR — any filter matches
var filter = builder.BuildOr();
```

### Conditional Filters (WhereIf)

`WhereIf` is essential for scripts that conditionally add filters:

```csharp
var filter = new FilterBuilder<TodoView>()
    .Where("IsArchived", false)                                          // always
    .WhereIf(ctx.HasPermission("view-all"), b => { /* no extra filter */ })
    .WhereIf(!ctx.HasPermission("view-all"), b =>
        b.WhereAny<ResponsibleView, Guid>("Responsibles", "Id", ctx.UserId))
    .WhereIf(customerIds.Count > 0, b =>
        b.Contains("CustomerId", customerIds))
    .Build();
```

## Building a Custom QueryBuilder

The helpers and `FilterBuilder` are designed as building blocks. Here's how you'd use them to build a domain-specific QueryBuilder:

```csharp
public class TodoQueryBuilder
{
    private readonly List<Expression<Func<TodoView, bool>>> _filters = [];

    public TodoQueryBuilder WhereResponsible(Guid userId)
    {
        _filters.Add(ExpressionHelper.Equal<TodoView, Guid>("ResponsibleId", userId));
        return this;
    }

    public TodoQueryBuilder WhereCustomerIn(IEnumerable<Guid> customerIds)
    {
        _filters.Add(ExpressionHelper.Contains<TodoView, Guid>("CustomerId", customerIds));
        return this;
    }

    public TodoQueryBuilder WhereStatus(string status)
    {
        _filters.Add(EnumExpressionHelper.EqualsAsString<TodoView>("Status", status));
        return this;
    }

    public TodoQueryBuilder ExcludeArchived()
    {
        _filters.Add(ExpressionHelper.Equal<TodoView, bool>("IsArchived", false));
        return this;
    }

    public Expression<Func<TodoView, bool>> Build()
    {
        if (_filters.Count == 0)
            return x => true;

        var result = _filters[0];
        for (int i = 1; i < _filters.Count; i++)
            result = ExpressionHelper.And(result, _filters[i]);

        return result;
    }
}
```

Usage with JsEval:

```csharp
var query = new TodoQueryBuilder();

engine.SetValue("ctx", accessContext);
engine.SetValue("query", query);
engine.Evaluate(preparedScript);

// Script called query.WhereResponsible(), WhereCustomerIn(), etc.
var expression = query.Build();

// Pass to ORM
var results = await session.Query<TodoView>()
    .Where(expression)
    .ToListAsync();
```
