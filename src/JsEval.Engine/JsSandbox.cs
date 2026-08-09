using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;
using SystemJson = System.Text.Json.JsonSerializer;
using Jint;
using Jint.Native;
using Jint.Native.Json;
using Jint.Native.Object;
using JsFunction = Jint.Native.Function.Function;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Executes untrusted JavaScript without exposing CLR objects, modules, host
/// functions, or an underlying engine handle. A fresh engine is created for
/// every execution so globals and prototype mutations cannot cross executions.
/// Only values explicitly supplied through <see cref="SetValue"/> persist, and
/// they cross the boundary exclusively as JSON. Instances are not thread-safe.
///
/// Script failures surface as <see cref="JsSandboxException"/>. Note that the
/// per-execution memory limit is enforced by Jint between statements, so a
/// single very large allocation (<c>'x'.repeat(n)</c>) can still spike host
/// memory before any limit observes it — run genuinely hostile code in a
/// process with an OS-level memory cap.
/// </summary>
public sealed class JsSandbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Unlike the shared JsonHelper this keeps nulls: a rule must be able to
        // tell "the host set this to null" from "the host never set it".
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private const string MainModuleName = "__sandbox_main__";

    private readonly JsSandboxOptions _options;
    private readonly JsonSerializerOptions _readOptions;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IJsModule> _modules = new(StringComparer.Ordinal);

    public JsSandbox()
        : this(new JsSandboxOptions())
    {
    }

    public JsSandbox(JsSandboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        _options = options;
        _readOptions = new JsonSerializerOptions(SerializerOptions) { MaxDepth = options.MaxDepth };
    }

    /// <summary>
    /// Serializes a value to JSON and makes the parsed plain JavaScript value
    /// available under <paramref name="name"/> during execution. No CLR object
    /// identity, methods, delegates, or property accessors cross the boundary.
    /// </summary>
    public void SetValue(string name, object? value)
    {
        ValidateName(name);
        var json = SystemJson.Serialize(value, SerializerOptions);

        var candidate = new Dictionary<string, string>(_values, StringComparer.Ordinal)
        {
            [name] = json
        };
        EnsureAggregateSize(candidate, _options.MaxInputBytes, "Serialized sandbox input", hostFault: true);

        _values[name] = json;
    }

    /// <summary>
    /// Returns a newly deserialized copy of a named sandbox value. The value is
    /// available before the first execution as well as after script mutations.
    /// </summary>
    public T? GetValue<T>(string name)
    {
        var json = GetValueAsJson(name);
        return SystemJson.Deserialize<T>(json, _readOptions);
    }

    /// <summary>Returns the JSON representation of a named sandbox value.</summary>
    public string GetValueAsJson(string name)
    {
        ValidateName(name);
        return _values.TryGetValue(name, out var json)
            ? json
            : throw new KeyNotFoundException($"Sandbox value '{name}' has not been set.");
    }

    /// <summary>
    /// Executes JavaScript with the currently configured serialized values. No
    /// host capabilities are provided. After execution, all configured values
    /// are serialized back to JSON for <see cref="GetValue{T}"/> and the next
    /// run. A name the script removed or set to <c>undefined</c> is kept and
    /// reads back as <c>null</c>, so the set of names the host established can
    /// never be reduced by a script.
    /// </summary>
    public void Execute(string script) => Run(script, asModule: false);

    /// <summary>
    /// Registers a host capability the script may <c>import</c> under
    /// <paramref name="name"/>. The module instance itself never enters the
    /// engine: each public method is exported as a plain JavaScript function
    /// whose arguments and return value cross as JSON, so CLR interop stays
    /// switched off and a script cannot climb from a returned value into the
    /// host object graph.
    ///
    /// Registering a module is a deliberate capability grant. Whatever the
    /// module can do, the script can do — that is the intended contract, not a
    /// sandbox escape. Two rules follow from the JSON boundary: return DTOs
    /// rather than internal objects, because a returned object's public
    /// properties are serialized into the script's reach, and keep the exported
    /// surface as small as possible.
    /// </summary>
    public void AddModule(string name, IJsModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Module name must not be empty.", nameof(name));
        if (!_modules.TryAdd(name, module))
            throw new ArgumentException($"A module named '{name}' is already registered.", nameof(name));
    }

    /// <summary>
    /// Executes the script as an ES module, so it can <c>import</c> the
    /// modules registered through <see cref="AddModule"/>. The value contract
    /// is identical to <see cref="Execute"/>.
    ///
    /// A module method returning a <see cref="Task"/> is awaited before its
    /// result crosses back, so the script sees a plain value and may
    /// <c>await</c> it harmlessly. That call blocks the executing thread:
    /// Jint cannot interrupt a host call in progress, so
    /// <paramref name="cancellationToken"/> and the execution timeout bound the
    /// script, not an individual module call. Give module implementations their
    /// own timeouts.
    /// </summary>
    public Task ExecuteAsync(string script, CancellationToken cancellationToken = default)
    {
        Run(script, asModule: true, cancellationToken);
        return Task.CompletedTask;
    }

    private void Run(string script, bool asModule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        EnsureSize(script, _options.MaxScriptBytes, "Sandbox script");

        var updated = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var engine = CreateEngine(cancellationToken);
            var parser = new JsonParser(engine);
            foreach (var (name, json) in _values)
                engine.SetValue(name, parser.Parse(json));

            if (asModule)
            {
                foreach (var (moduleName, module) in _modules)
                    RegisterModule(engine, moduleName, module);

                engine.Modules.Add(MainModuleName, builder => builder.AddSource(script));
                engine.Modules.Import(MainModuleName);
            }
            else
            {
                engine.Execute(script);
            }

            var names = new List<string>(_values.Keys);
            var results = ReadResolvedValues(engine, names);
            var serializer = new Jint.Native.Json.JsonSerializer(engine);

            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                var value = results[i];

                // A removed or undefined value becomes null rather than
                // vanishing, so a later Execute and GetValue still see the name.
                if (value.IsUndefined())
                {
                    updated[name] = "null";
                    continue;
                }

                EnsureDepth(name, value);

                var json = serializer.Serialize(value);
                if (json.IsUndefined())
                    throw new JsSandboxException(
                        $"Sandbox value '{name}' is no longer JSON-serializable after execution.");

                updated[name] = json.AsString();
            }
        }
        catch (JsSandboxException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JsSandboxException($"Sandbox execution failed: {ex.Message}", ex);
        }

        EnsureAggregateSize(updated, _options.MaxOutputBytes, "Serialized sandbox output", hostFault: false);

        _values.Clear();
        foreach (var (name, json) in updated)
            _values[name] = json;
    }

    /// <summary>
    /// Exports a module's public methods as plain JavaScript functions. The
    /// module instance is never handed to Jint, which is what lets the sandbox
    /// keep CLR interop disabled entirely while still offering capabilities.
    /// Properties are deliberately not exported — they would have to hand out
    /// a value with no call site to normalize, and a method is the explicit form.
    /// </summary>
    private void RegisterModule(Jint.Engine engine, string name, IJsModule module)
    {
        var methods = module.GetType().GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        engine.Modules.Add(name, builder =>
        {
            var exported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in methods)
            {
                if (method.IsSpecialName) continue;      // property accessors
                if (!exported.Add(method.Name)) continue; // first overload wins
                builder.ExportFunction(method.Name, InvokerFor(engine, module, method));
            }
        });
    }

    /// <summary>
    /// Marshals one module call across the JSON boundary in both directions:
    /// JavaScript arguments are serialized and deserialized into the declared
    /// parameter types, and the return value is serialized and re-parsed into a
    /// plain JavaScript value. No CLR object ever becomes a <see cref="JsValue"/>.
    /// </summary>
    private Func<JsValue[], JsValue> InvokerFor(Jint.Engine engine, object module, MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parser = new JsonParser(engine);
        var serializer = new Jint.Native.Json.JsonSerializer(engine);

        return arguments =>
        {
            var call = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                // A JsValue parameter receives the script's value unconverted.
                // This is the channel for things JSON cannot carry — chiefly an
                // arrow function a module wants to translate rather than run,
                // as JsExpressionTranslator does for IQueryable rules. The value
                // stays sandbox-side script data; a module that chooses to
                // invoke it is running sandbox code, not escaping it.
                if (parameters[i].ParameterType == typeof(JsValue))
                {
                    call[i] = i < arguments.Length ? arguments[i] : JsValue.Undefined;
                    continue;
                }

                if (i >= arguments.Length || arguments[i].IsUndefined())
                {
                    call[i] = parameters[i].HasDefaultValue
                        ? parameters[i].DefaultValue
                        : parameters[i].ParameterType.IsValueType
                            ? Activator.CreateInstance(parameters[i].ParameterType)
                            : null;
                    continue;
                }

                var argumentJson = serializer.Serialize(arguments[i]);
                call[i] = argumentJson.IsUndefined()
                    ? null
                    : SystemJson.Deserialize(argumentJson.AsString(), parameters[i].ParameterType, _readOptions);
            }

            object? result;
            try
            {
                result = method.Invoke(module, call);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw new JsSandboxException(
                    $"Module '{method.DeclaringType?.Name}' failed in '{method.Name}': {ex.InnerException.Message}",
                    ex.InnerException);
            }

            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
                var taskType = task.GetType();
                result = taskType.IsGenericType
                    ? taskType.GetProperty(nameof(Task<int>.Result))!.GetValue(task)
                    : null;
            }

            return result is null
                ? JsValue.Undefined
                : parser.Parse(SystemJson.Serialize(result, SerializerOptions));
        };
    }

    /// <summary>
    /// Reads every named value the way the script itself sees it. A top-level
    /// <c>let</c> or <c>const</c> shadows the global property the value was
    /// installed as, and reading that property directly would silently hand the
    /// host the pre-execution value instead of what the rule produced. One
    /// array expression resolves them all through the normal scope chain, and
    /// the <c>typeof</c> guard keeps a name the script deleted from raising a
    /// <c>ReferenceError</c>.
    /// </summary>
    private static JsValue[] ReadResolvedValues(Jint.Engine engine, List<string> names)
    {
        if (names.Count == 0)
            return [];

        var expression = new StringBuilder("[");
        for (var i = 0; i < names.Count; i++)
        {
            if (i > 0) expression.Append(',');
            expression.Append(CultureInfo.InvariantCulture,
                $"(typeof {names[i]} === 'undefined' ? undefined : {names[i]})");
        }
        expression.Append(']');

        var array = engine.Evaluate(expression.ToString()).AsArray();
        var results = new JsValue[names.Count];
        for (var i = 0; i < names.Count; i++)
            results[i] = array[(uint)i];

        return results;
    }

    /// <summary>
    /// Rejects a value whose nesting would take Jint's recursive JSON
    /// serializer past the .NET stack. See <see cref="JsValueDepth"/>.
    /// </summary>
    private void EnsureDepth(string name, JsValue root)
    {
        if (JsValueDepth.Exceeds(root, _options.MaxDepth))
            throw new JsSandboxException(
                $"Sandbox value '{name}' nests deeper than the configured limit of {_options.MaxDepth}.");
    }

    private Jint.Engine CreateEngine(CancellationToken cancellationToken = default)
    {
        var jintOptions = new Options()
            .Strict()
            .DisableStringCompilation()
            .Culture(CultureInfo.InvariantCulture)
            .LocalTimeZone(TimeZoneInfo.Utc)
            .TimeoutInterval(_options.ExecutionTimeout)
            .MaxStatements(_options.MaxStatements)
            .LimitMemory(_options.MemoryLimitBytes)
            .LimitRecursion(_options.MaxRecursionDepth)
            .CancellationToken(cancellationToken);

        jintOptions.Constraints.MaxArraySize = _options.MaxArraySize;
        jintOptions.Constraints.MaxExecutionStackCount = _options.MaxExecutionStackCount;
        jintOptions.Constraints.RegexTimeout = _options.RegexTimeout;
        jintOptions.Constraints.PromiseTimeout = _options.ExecutionTimeout;
        jintOptions.Json.MaxParseDepth = _options.MaxDepth;

        // Explicit even though Jint currently defaults these to false. A future
        // default change must not silently widen the sandbox's capability set.
        jintOptions.Interop.Enabled = false;
        jintOptions.Interop.AllowGetType = false;
        jintOptions.Interop.AllowSystemReflection = false;
        jintOptions.Interop.AllowWrite = false;
        jintOptions.Interop.AllowOperatorOverloading = false;
        jintOptions.Interop.AllowedAssemblies.Clear();
        jintOptions.Interop.TypeResolver = new TypeResolver
        {
            MemberFilter = static _ => false
        };
        jintOptions.Interop.WrapObjectHandler = static (_, _, _) =>
            throw new InvalidOperationException("CLR objects cannot be exposed inside JsSandbox.");

        var engine = new Jint.Engine(jintOptions);

        // Shared-memory atomics can intentionally block or spin. They provide
        // no value for data rules, so remove them from the capability surface.
        engine.SetValue("Atomics", JsValue.Undefined);
        engine.SetValue("SharedArrayBuffer", JsValue.Undefined);

        // Built-in prototypes are frozen so a rule cannot reach the host's
        // result through inherited members. JSON.stringify reads inherited
        // index properties on sparse arrays, so `Array.prototype[1] = 'x'`
        // would otherwise inject values the rule never assigned.
        engine.Execute("""
            for (const ctor of [Object, Array, String, Number, Boolean, Date, RegExp, Function, Error]) {
                Object.freeze(ctor.prototype);
            }
            """);

        return engine;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sandbox value name must not be empty.", nameof(name));

        if (!IsValidIdentifier(name))
            throw new ArgumentException(
                $"Sandbox value name '{name}' is not a valid JavaScript identifier.", nameof(name));

        if (ReservedNames.Contains(name))
            throw new ArgumentException(
                $"Sandbox value name '{name}' is a reserved JavaScript name. " +
                "Binding it would either fail or silently discard the value.", nameof(name));
    }

    private static bool IsValidIdentifier(string name)
    {
        if (name.Length == 0 || (!char.IsLetter(name[0]) && name[0] is not '_' and not '$'))
            return false;

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c is not '_' and not '$')
                return false;
        }

        return true;
    }

    // Assigning to the first three is silently ignored (they are non-writable
    // globals), which would hand the host back a null instead of its own value;
    // globalThis would replace the global object; the rest cannot be bound as
    // identifiers at all.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "undefined", "NaN", "Infinity", "globalThis",
        "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for",
        "function", "if", "import", "in", "instanceof", "new", "null", "return", "super",
        "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with",
        "let", "static", "yield", "await", "implements", "interface", "package", "private",
        "protected", "public"
    };

    /// <param name="hostFault">
    /// Oversized input is a bug in the calling code and stays an
    /// <see cref="ArgumentException"/>; oversized output was produced by the
    /// script and belongs to the <see cref="JsSandboxException"/> contract.
    /// </param>
    private static void EnsureAggregateSize(
        IReadOnlyDictionary<string, string> values, int limit, string name, bool hostFault)
    {
        var bytes = 0;
        foreach (var (valueName, json) in values)
        {
            bytes = checked(bytes + Encoding.UTF8.GetByteCount(valueName));
            bytes = checked(bytes + Encoding.UTF8.GetByteCount(json));
        }

        if (bytes <= limit)
            return;

        var message = $"{name} is {bytes} bytes; the configured limit is {limit} bytes.";
        throw hostFault ? new ArgumentException(message) : new JsSandboxException(message);
    }

    private static void EnsureSize(string value, int limit, string name)
    {
        var bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes > limit)
            throw new ArgumentException($"{name} is {bytes} bytes; the configured limit is {limit} bytes.");
    }

    private static void ValidateOptions(JsSandboxOptions options)
    {
        if (options.ExecutionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "ExecutionTimeout must be positive.");
        if (options.RegexTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "RegexTimeout must be positive.");
        if (options.MaxStatements <= 0 || options.MemoryLimitBytes <= 0 ||
            options.MaxRecursionDepth <= 0 || options.MaxExecutionStackCount <= 0 ||
            options.MaxArraySize == 0 || options.MaxScriptBytes <= 0 ||
            options.MaxInputBytes <= 0 || options.MaxOutputBytes <= 0 ||
            options.MaxDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "All sandbox limits must be positive.");
        }
    }
}
