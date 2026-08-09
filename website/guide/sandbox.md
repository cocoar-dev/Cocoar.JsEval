# JSON-only Sandbox

`JsSandbox` is the execution surface for tenant- or end-user-authored rules. It
is intentionally separate from `JsEngine` and cannot register modules, expose
CLR values, enable host globals, or provide access to the underlying Jint
engine.

## Empty sandbox

```csharp
var sandbox = new JsSandbox();
sandbox.Execute("const result = 20 + 22;");
```

Standard JavaScript language primitives remain available. Host capabilities
such as `System`, `importNamespace`, `fetch`, `require`, `NewObject`, `console`
and timers are absent. `eval`, the `Function` constructor, `Atomics` and
`SharedArrayBuffer` are disabled.

Culture and timezone are fixed to invariant and UTC, so string formatting and
date arithmetic do not vary with the host's locale. This is not full
determinism: `Date.now()` still returns real wall-clock time. Pass a timestamp
in as a value if a rule must be reproducible.

Built-in prototypes are frozen. `Object.prototype.x = 1` throws instead of
succeeding — beyond discouraging prototype tricks, this closes a path by which
a rule could alter its own result, since `JSON.stringify` reads inherited index
properties when serializing a sparse array.

Every call creates a fresh engine. Globals and prototype changes made by one
rule cannot affect a later rule.

## Named serialized values

```csharp
public sealed class Customer
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public bool IsAdult { get; set; }
}

var original = new Customer { Name = " Alice ", Age = 17 };

sandbox.SetValue("customer", original);
sandbox.SetValue("minimumAge", 18);

sandbox.Execute("""
    customer.Name = customer.Name.trim();
    customer.IsAdult = customer.Age >= minimumAge;
    """);

var changed = sandbox.GetValue<Customer>("customer");
```

The data flow is:

1. `SetValue` serializes every supplied value to JSON immediately.
2. A fresh Jint engine parses each JSON value under its configured global name.
3. The rule may change those plain JavaScript values.
4. Each configured value is serialized back to JSON after execution.
5. `GetValue<T>` returns a newly deserialized value of the requested type.

`original` is unchanged. Its CLR identity, methods, property setters and other
non-JSON members never enter the sandbox. Multiple POCOs, collections and
primitive values can be supplied.

A value name must be a valid JavaScript identifier, so the rule can reference
it directly. Names that JavaScript will not bind — reserved words, and the
non-writable globals `undefined`, `NaN`, `Infinity` and `globalThis` — are
rejected with an `ArgumentException` rather than silently losing the value.

C# `null` arrives as JavaScript `null`, not as a missing property, so a rule
can tell "the host set this to null" from "the host never set it".

Only values registered through `SetValue` persist between calls. Every
`Execute` still uses a fresh engine, so other globals and prototype mutations
are discarded.

The set of names is owned by the host and a rule cannot shrink it. Redeclaring
a value with `let` or `const` is read back as what the rule produced, and a
name the rule deletes or sets to `undefined` reads back as `null` while
remaining available to the next `Execute`.

## Errors

Everything that goes wrong during execution surfaces as `JsSandboxException`,
with the underlying error kept as `InnerException`:

```csharp
try
{
    sandbox.Execute(rule);
}
catch (JsSandboxException ex)
{
    logger.LogWarning(ex, "Rule {RuleId} failed", ruleId);
}
```

That covers script errors, the execution timeout, the statement, memory,
recursion and regex limits, nesting-depth and output-size violations, and
values a rule left in a non-serializable state. A failed `Execute` changes
nothing: every value keeps its previous content.

Misuse by the host is deliberately not wrapped. An invalid value name, an
oversized input or an oversized script throws `ArgumentException`, because that
is a bug in the calling code rather than a rule failure.

## Capability modules

`SetValue` is intentionally a data channel, not an extension mechanism. If a
host wants to make selected application functionality available to a rule, the
planned extension point is an explicit allowlist of JsEval modules.

```csharp
var sandbox = new JsSandboxBuilder(services)
    .AddModule<CustomerLookupModule>()
    .Build();

sandbox.SetValue("customer", customer);

await sandbox.ExecuteAsync("""
    import * as customers from "customers";

    customer.IsDuplicate = await customers.ExistsByEmail(customer.Email);
    """);
```

::: info Planned API
Module registration and `ExecuteAsync` are not part of `JsSandbox` yet. The
example documents the intended capability model. The current `JsSandbox`
accepts only JSON values and synchronous scripts.
:::

Modules are capabilities: registering a module is an explicit decision by the
host to grant every script in that sandbox the operations exported by that
module. A module may internally use a database, HTTP client, filesystem or
reflection. If the host registers such a module, that access is intentionally
available and is not considered a sandbox escape.

The security contract is:

> A sandboxed script can work with its named JSON values and call only the
> modules explicitly registered by the host.

The safe default is an empty module allowlist. Sandbox module support must not
enable `require`, general CLR access or arbitrary CLR values through
`SetValue`. Module arguments and return values should cross a controlled JSON
boundary so that a returned CLR object cannot accidentally expose additional
methods, properties or internal dependencies.

For example, a customer lookup module may depend on an internal repository,
but only its deliberately exported lookup functions should be visible to the
script. Conversely, registering a reflection module deliberately grants
reflection functionality. Reviewing and securing the implementation of an
allowed module remains the host application's responsibility.

TypeScript declarations can describe the allowed module API and provide
autocomplete to rule authors. TypeScript improves the authoring experience; it
does not create the security boundary. The module allowlist defines the
capabilities, while `JsSandbox` provides execution and resource limits.

## Default limits

| Limit | Default |
|---|---:|
| Execution timeout | 250 ms |
| Regex timeout | 50 ms |
| Statements | 100,000 |
| Allocated memory | 16 MiB |
| Recursion depth | 64 |
| Execution stack | 256 |
| Array size | 10,000 |
| Script size | 64 KiB |
| Input/output size | 256 KiB each |
| Nesting depth | 64 |

Hosts can tighten these resource limits with `JsSandboxOptions`. The options do
not contain switches for enabling CLR interop or modules.

`MaxDepth` is a safety limit rather than a preference. JSON serialization
recurses once per level, so an unbounded structure would exhaust the .NET stack
and terminate the process with an uncatchable `StackOverflowException`. Values
are therefore checked before serialization and rejected with a
`JsSandboxException`. The default matches Jint's own JSON parse depth and
`System.Text.Json`'s read depth, so all three agree on what is representable.

::: warning Memory is not fully bounded in-process
`MemoryLimitBytes` is enforced between statements, which catches allocation
spread over a loop but not a single large one. `'x'.repeat(800 * 1024 * 1024)`
commits the whole string before any limit observes it — the run then fails, but
the memory was already taken. No Jint setting closes this.

For input you do not control, run the sandbox in a process with an OS-level
memory cap (a Windows job object, a cgroup, or a container limit). That same
boundary is also the only defense against a vulnerability in Jint or the .NET
runtime itself.
:::
