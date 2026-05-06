using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Acornima.Ast;
using Cocoar.JsEval;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using Jint.Native.Json;
using Jint.Native.Object;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.JsEval.Engine;

public sealed class JsEngine : IScriptEngine, IDisposable, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IJsModuleRegistry _moduleRegistry;
    private readonly ILogger<JsEngine> _logger;

    public JsEngineOptions Options { get; }

    /// <summary>
    /// The underlying Jint engine. Exposed so advanced consumers (e.g. custom LINQ
    /// translators, direct Jint interop) can reach Jint APIs that the higher-level
    /// facade intentionally hides.
    /// </summary>
    public Jint.Engine UnderlyingEngine => _engine;

    private readonly Jint.Engine _engine;
    private readonly JsonParser _jsonParser;
    private readonly JsonSerializer _jsonSerializer;

    private readonly Dictionary<Type, Func<object>> _providedTypeFactories = [];
    private readonly List<string> _useTaggedModules = [];
    private readonly Dictionary<Type, object> _instantiatedModules = [];

    // Per-module-type cache of the PRE-PARSED wrapper AST. The wrapper is the
    // JavaScript bridge that re-exports the CLR module's methods/properties as
    // ES-module exports. Building the source is cheap; PARSING it for every
    // fresh engine is the expensive part — Prepared<Module> sidesteps that by
    // parsing exactly once globally and handing the AST to each engine via
    // ModuleBuilder.AddModule.
    // Cached reflection info per module type — GetMethods/GetProperties are
    // not free and the result is invariant for the type's lifetime.
    private sealed record ModuleShape(PropertyInfo[] Properties, MethodInfo[] Methods);
    private static readonly ConcurrentDictionary<Type, ModuleShape> ModuleShapes = new();

    // Pre-parsed JS scripts — parsed once, reused across all engine instances
    private static readonly Prepared<Script> ConsoleScript = Jint.Engine.PrepareScript(@"
var console = {
    log:   function() { __log_info(Array.prototype.slice.call(arguments).join(' ')); },
    info:  function() { __log_info(Array.prototype.slice.call(arguments).join(' ')); },
    warn:  function() { __log_warn(Array.prototype.slice.call(arguments).join(' ')); },
    error: function() { __log_error(Array.prototype.slice.call(arguments).join(' ')); },
    debug: function() { __log_debug(Array.prototype.slice.call(arguments).join(' ')); }
};
");

    private static readonly Prepared<Script> StructuredCloneScript = Jint.Engine.PrepareScript(
        "function structuredClone(obj) { return JSON.parse(JSON.stringify(obj)); }");

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private readonly ConcurrentDictionary<int, CancellationTokenSource> _timers = new();
    private int _nextTimerId;
    private bool _modulesAdded;

    // Main-script module cache: dedup identical scripts so Top-Level code runs
    // only once per unique content (ES-module semantics, not REPL-style re-execution).
    // Exports stabilise — callers that want "fresh per call" work should use
    // `export function foo()` and invoke it via InvokeFunction.
    private readonly Dictionary<string, string> _sourceModuleNames = new(StringComparer.Ordinal);
    private readonly Dictionary<JsPreparedModule, string> _preparedModuleNames = new();
    private int _nextMainModuleId;

    private ObjectInstance? _mainModule;

    public JsEngine(IServiceProvider serviceProvider, IJsModuleRegistry moduleRegistry, JsEngineOptions options, ILogger<JsEngine>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _moduleRegistry = moduleRegistry;
        _logger = logger ?? NullLogger<JsEngine>.Instance;
        Options = options;
        _engine = new Jint.Engine(Options.JintOptions.CancellationToken(_cancellationTokenSource.Token));
        _jsonParser = new JsonParser(_engine);
        _jsonSerializer = new JsonSerializer(_engine);
        Initialize();
    }

    /// <summary>
    /// Resolve a NewObject(name, ...) call. First consults
    /// <see cref="JsEngineOptions.TypeAliases"/> for user-registered short names;
    /// if not found, falls back to the legacy <see cref="TypeHelper.CreateObject"/>
    /// path (which still handles TypeScript-specific aliases like "date" →
    /// System.DateTime and assembly-qualified lookups).
    /// </summary>
    private object? ResolveAndCreate(string typeName, object[] parameters)
    {
        if (!string.IsNullOrEmpty(typeName) && Options.TypeAliases.TryGetValue(typeName, out var aliasedType))
        {
            parameters ??= Array.Empty<object>();
            return parameters.Length > 0
                ? Activator.CreateInstance(aliasedType, parameters)
                : Activator.CreateInstance(aliasedType);
        }
        return TypeHelper.CreateObject(typeName, parameters);
    }

    private void Initialize()
    {
        _engine.SetValue("exit", new Action(Stop));
        _engine.SetValue("NewObject", new Func<string, object[], object?>(ResolveAndCreate));
        _engine.SetValue("require", new Func<string, JsValue>(Require));

        RegisterConsole();
        RegisterTimers();
        RegisterWebApis();

        _engine.Execute(StructuredCloneScript);

        if (Options.FetchEnabled)
            Fetch.FetchHandler.Register(_engine);

        if (Options.DiscriminatorMappings.Count > 0 || Options.TypeAliases.Count > 0)
            _engine.SetValue("Type", new JsTypeGlobal(Options.DiscriminatorMappings, Options.TypeAliases, Options.NamespaceMappings));

        foreach (var configurator in Options.EngineConfigurators)
            configurator(_engine);
    }

    private void RegisterConsole()
    {
#pragma warning disable CA1848, CA1873 // LoggerMessage.Define not needed for console bridge
        _engine.SetValue("__log_info", new Action<string>(msg => _logger.LogInformation("{Message}", msg)));
        _engine.SetValue("__log_warn", new Action<string>(msg => _logger.LogWarning("{Message}", msg)));
        _engine.SetValue("__log_error", new Action<string>(msg => _logger.LogError("{Message}", msg)));
        _engine.SetValue("__log_debug", new Action<string>(msg => _logger.LogDebug("{Message}", msg)));
#pragma warning restore CA1848, CA1873

        _engine.Execute(ConsoleScript);
    }

    private void RegisterTimers()
    {
        _engine.SetValue("setTimeout", new Func<JsValue, int, int>((callback, delay) =>
        {
            var id = Interlocked.Increment(ref _nextTimerId);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
            _timers[id] = cts;

            _ = Task.Delay(Math.Max(0, delay), cts.Token).ContinueWith(static (t, state) =>
            {
                var (timers, timerId, cb, engine) = ((ConcurrentDictionary<int, CancellationTokenSource>, int, JsValue, Jint.Engine))state!;
                if (!t.IsCanceled)
                {
                    timers.TryRemove(timerId, out _);
                    engine.Invoke(cb);
                }
            }, (_timers, id, callback, _engine), TaskScheduler.Default);

            return id;
        }));

        _engine.SetValue("setInterval", new Func<JsValue, int, int>((callback, delay) =>
        {
            var id = Interlocked.Increment(ref _nextTimerId);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
            _timers[id] = cts;

            _ = Task.Run(async () =>
            {
                var interval = Math.Max(0, delay);
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(interval, cts.Token).ConfigureAwait(false);
                    if (!cts.Token.IsCancellationRequested)
                        _engine.Invoke(callback);
                }
            }, cts.Token);

            return id;
        }));

        _engine.SetValue("clearTimeout", new Action<int>(id =>
        {
            if (_timers.TryRemove(id, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }));

        _engine.SetValue("clearInterval", new Action<int>(id =>
        {
            if (_timers.TryRemove(id, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }));
    }

    private void RegisterWebApis()
    {
        // atob / btoa — Base64 encoding/decoding
        _engine.SetValue("btoa", new Func<string, string>(str =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(str))));

        _engine.SetValue("atob", new Func<string, string>(str =>
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(str))));

        // performance.now() — high-resolution timestamp
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _engine.SetValue("__perf_now", new Func<double>(() => stopwatch.Elapsed.TotalMilliseconds));
        _engine.Execute(PerformanceScript);

        // TextEncoder / TextDecoder — UTF-8 encoding/decoding
        _engine.SetValue("__te_encode", new Func<string, byte[]>(str =>
            System.Text.Encoding.UTF8.GetBytes(str)));

        _engine.SetValue("__td_decode", new Func<byte[], string>(bytes =>
            System.Text.Encoding.UTF8.GetString(bytes)));

        _engine.Execute(TextEncoderDecoderScript);
    }

    private static readonly Prepared<Script> PerformanceScript = Jint.Engine.PrepareScript(
        "var performance = { now: function() { return __perf_now(); } };");

    private static readonly Prepared<Script> TextEncoderDecoderScript = Jint.Engine.PrepareScript(@"
function TextEncoder() {}
TextEncoder.prototype.encode = function(str) { return __te_encode(str); };

function TextDecoder(label) { this.encoding = label || 'utf-8'; }
TextDecoder.prototype.decode = function(buf) { return __td_decode(buf); };
");

    public void Stop() => _cancellationTokenSource.Cancel();

    // --- JSON (implements IScriptEngine for module injection) ---

    public object? JsonParse(string? json) => json is null ? null : _jsonParser.Parse(json);

    public string JsonStringify(object? value) => value switch
    {
        JsValue jsValue => _jsonSerializer.Serialize(jsValue).AsString(),
        JsonNode jsonNode => jsonNode.ToJsonString(),
        _ => JsonHelper.ToJson(value)
    };

    public object? ConvertToDefaultObject(object? value)
    {
        if (value is null) return null;
        return JsonParse(JsonStringify(value));
    }

    // --- Values ---

    public void SetValue(string name, object value)
    {
        switch (value)
        {
            case string str: _engine.SetValue(name, str); break;
            case double dbl: _engine.SetValue(name, dbl); break;
            case bool b: _engine.SetValue(name, b); break;
            // JS numbers are IEEE-754 double; route integer/float CLR primitives
            // straight to the double setter instead of the reflective FromObject path.
            case int i: _engine.SetValue(name, (double)i); break;
            case long l: _engine.SetValue(name, (double)l); break;
            case float f: _engine.SetValue(name, (double)f); break;
            case decimal m: _engine.SetValue(name, (double)m); break;
            default: _engine.SetValue(name, JsValue.FromObject(_engine, value)); break;
        }
    }

    public string GetValueAsJson(string name) =>
        _jsonSerializer.Serialize(InternalGetValue(name)).AsString();

    public T? GetValue<T>(string name) => ConvertJsValue<T>(InternalGetValue(name));

    private T? ConvertJsValue<T>(JsValue jsValue)
    {
        if (jsValue.IsUndefined() || jsValue.IsNull()) return default;

        // Fast path: primitives — no JSON round-trip
        if (typeof(T) == typeof(string)) return (T)(object)jsValue.AsString();
        if (typeof(T) == typeof(int))    return (T)(object)(int)jsValue.AsNumber();
        if (typeof(T) == typeof(double)) return (T)(object)jsValue.AsNumber();
        if (typeof(T) == typeof(bool))   return (T)(object)jsValue.AsBoolean();
        if (typeof(T) == typeof(long))   return (T)(object)(long)jsValue.AsNumber();
        if (typeof(T) == typeof(float))  return (T)(object)(float)jsValue.AsNumber();

        // Reference types: ToObject() preserves the original .NET reference
        // (e.g. IQueryable<T>, custom classes set via SetValue).
        // Only fall back to JSON for value types that ToObject() can't produce directly.
        var obj = jsValue.ToObject();
        if (obj is T typed) return typed;

        return JsonHelper.ToObject<T>(_jsonSerializer.Serialize(jsValue).AsString());
    }

    public object GetValue(string name) =>
        InternalGetValue(name);

    private JsValue InternalGetValue(string name) =>
        _mainModule is not null ? _mainModule.Get(name) : _engine.GetValue(name);

    // --- Evaluate: Lightweight execution (no module system) ---
    // Use Evaluate when you control the scripts and know they don't need
    // import/export or modules. Ideal for policy evaluation, rule engines,
    // and simple expressions where performance matters.

    /// <summary>
    /// Pre-parses a JavaScript script for repeated execution via <see cref="Evaluate(JsPreparedScript)"/>.
    /// The returned object is thread-safe and can be cached globally (e.g., per role, per policy).
    /// Parse cost is ~7 µs — execution of the prepared script is &lt; 1 µs on a reused engine.
    /// </summary>
    public static JsPreparedScript Prepare(string script) =>
        new(Jint.Engine.PrepareScript(script));

    /// <summary>
    /// Pre-parses an ES module script for repeated execution via <see cref="ExecuteAsync(JsPreparedModule)"/>.
    /// Supports <c>import</c> / <c>export</c>. Parse cost is paid once; each execution runs
    /// top-level statements fresh (so <c>export const guid = common.Guid.New()</c> yields a new GUID per call).
    /// The returned object is thread-safe and can be cached globally.
    /// </summary>
    public static JsPreparedModule PrepareModule(string script) =>
        new(Jint.Engine.PrepareModule(script));

    /// <summary>
    /// Evaluates a plain JavaScript script synchronously. No module system, no import/export, no async/await.
    /// Variables set via <see cref="SetValue"/> are available as globals.
    /// Use this when you know the script content and it doesn't need modules or async.
    /// For best performance with repeated execution, use <see cref="Prepare"/> + <see cref="Evaluate(JsPreparedScript)"/>.
    /// </summary>
    public void Evaluate(string script) =>
        _engine.Execute(script);

    /// <summary>
    /// Evaluates a JS expression and returns the resulting <see cref="JsValue"/>.
    /// Use this when the script is a single expression whose value you need
    /// (e.g. <c>"(u) => u.Name === 'A'"</c> for the LINQ translator).
    /// Unlike <see cref="Evaluate(string)"/> which uses statement semantics, this
    /// maps to Jint's <c>Evaluate</c> — the expression's value is returned.
    /// </summary>
    public JsValue EvaluateExpression(string script) =>
        _engine.Evaluate(script);

    /// <summary>
    /// Evaluates a pre-parsed script synchronously. Avoids re-parsing on every call.
    /// This is the fastest execution path (~0.8 µs on a reused engine for simple scripts).
    /// Use <see cref="Prepare"/> to create the prepared script.
    /// </summary>
    public void Evaluate(JsPreparedScript prepared) =>
        _engine.Execute(prepared.Prepared);

    /// <summary>
    /// Evaluates a plain JavaScript script asynchronously. Supports top-level await but no module system.
    /// Use this when the script may contain await expressions but doesn't need import/export.
    /// </summary>
    public async Task EvaluateAsync(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return;

        try
        {
            await _engine.EvaluateAsync(script, cancellationToken: _cancellationTokenSource.Token);
        }
        catch (ExecutionCanceledException)
        {
            // Script was cancelled via Stop()
        }
        catch (Exception exception)
        {
            throw exception.GetBaseException();
        }
    }

    // --- ExecuteAsync: Full execution with ES module system ---
    // Use ExecuteAsync when scripts need import/export, modules, or you don't
    // know what the script will contain. This is the default for most use cases.

    /// <summary>
    /// Executes a script as an ES module with full module system support.
    /// Supports import/export, registered modules, async/await, and all JS features.
    /// This is the standard execution method — use this when you don't know what the script contains.
    /// </summary>
    public async Task ExecuteAsync(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return;

        try
        {
            if (!_modulesAdded)
            {
                AddModules();
                _modulesAdded = true;
            }

            if (!_sourceModuleNames.TryGetValue(script, out var moduleName))
            {
                moduleName = $"__main_{_nextMainModuleId++}__";
                _engine.Modules.Add(moduleName, script);
                _sourceModuleNames[script] = moduleName;
            }

            // Use async path only when needed — avoid exception-driven fallback
            if (script.Contains("await", StringComparison.Ordinal))
            {
                var result = await _engine.EvaluateAsync(
                    $"import('{moduleName}')",
                    cancellationToken: _cancellationTokenSource.Token);
                _mainModule = result.AsObject();
            }
            else
            {
                _mainModule = _engine.Modules.Import(moduleName);
            }
        }
        catch (ExecutionCanceledException)
        {
            // Script was cancelled via Stop()
        }
        catch (Exception exception)
        {
            throw exception.GetBaseException();
        }
    }

    /// <summary>
    /// Executes a pre-parsed ES module script. Avoids the per-call parse cost of
    /// <see cref="ExecuteAsync(string)"/>. Follows standard ES-module semantics:
    /// top-level code runs once per unique module; repeated executions of the same
    /// <see cref="JsPreparedModule"/> instance on the same engine return the cached
    /// module namespace. For per-call work, export a function and invoke it via
    /// <see cref="InvokeFunction"/>.
    /// </summary>
    public async Task ExecuteAsync(JsPreparedModule prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        try
        {
            if (!_modulesAdded)
            {
                AddModules();
                _modulesAdded = true;
            }

            if (!_preparedModuleNames.TryGetValue(prepared, out var moduleName))
            {
                moduleName = $"__main_{_nextMainModuleId++}__";
                var preparedModule = prepared.Prepared;
                _engine.Modules.Add(moduleName, builder => builder.AddModule(in preparedModule));
                _preparedModuleNames[prepared] = moduleName;
            }

            var result = await _engine.EvaluateAsync(
                $"import('{moduleName}')",
                cancellationToken: _cancellationTokenSource.Token);
            _mainModule = result.AsObject();
        }
        catch (ExecutionCanceledException)
        {
            // Script was cancelled via Stop()
        }
        catch (Exception exception)
        {
            throw exception.GetBaseException();
        }
    }

    // --- Functions ---

    public JsFunction? GetFunction(string name)
    {
        var val = InternalGetValue(name);
        if (val is ScriptFunction func && func.FunctionDeclaration is { } decl)
        {
            var paramNames = new List<string>();
            foreach (var param in decl.Params)
            {
                if (param is Identifier identifier)
                    paramNames.Add(identifier.Name);
            }
            return new JsFunction(decl.Id?.Name ?? name, paramNames, InvokeFunction);
        }
        return null;
    }

    public object InvokeFunction(string name, params object[] args) =>
        _mainModule is not null
            ? _engine.Invoke(_mainModule.Get(name), args)
            : _engine.Invoke(name, args);

    // --- Modules ---

    public void AddModuleParameterInstance(Type type, Func<object> factory) =>
        _providedTypeFactories[type] = factory;

    public void AddTaggedModules(params string[] tags) =>
        _useTaggedModules.AddRange(tags);

    public T? GetModuleState<T>() =>
        _instantiatedModules.TryGetValue(typeof(T), out var module) ? (T)module : default;

    private void AddModules()
    {
        var moduleDefinitions = _moduleRegistry.GetRegisteredModuleDefinitions();

        if (_useTaggedModules.Count > 0)
        {
            moduleDefinitions = moduleDefinitions.Where(md =>
                md.Tags != null && md.Tags.Any(argTag => _useTaggedModules.Contains(argTag, StringComparer.OrdinalIgnoreCase)));
        }

        foreach (var moduleDefinition in moduleDefinitions)
        {
            // Register each module directly via ModuleBuilder — no JS wrapper
            // source is generated or parsed. The module's public members become
            // named exports straight away, so `import * as x from 'foo'` and
            // `import { member } from 'foo'` resolve against pre-bound JsValues
            // instead of an ES-module wrapper that chains through require().
            var definition = moduleDefinition;
            _engine.Modules.Add(definition.Name, builder => BuildModule(builder, definition));
        }
    }

    private void BuildModule(Jint.Runtime.Modules.ModuleBuilder builder, IJsModuleDefinition definition)
    {
        var instance = _moduleRegistry.BuildModuleInstance(
            definition.Name, _serviceProvider, this, _providedTypeFactories, _useTaggedModules);
        _instantiatedModules[instance.GetType()] = instance;

        var shape = ModuleShapes.GetOrAdd(definition.ModuleType, static type => new ModuleShape(
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)));

        foreach (var property in shape.Properties)
        {
            var value = property.GetValue(instance);
            if (value is not null)
                builder.ExportObject(property.Name, value);
        }

        var processedMethods = new HashSet<string>();
        foreach (var method in shape.Methods)
        {
            if (method.IsSpecialName) continue; // skip property get_/set_ accessors
            if (!processedMethods.Add(method.Name)) continue; // first overload wins (matches legacy behavior)
            builder.ExportFunction(method.Name, InvokerFor(instance, method));
        }
    }

    private Func<JsValue[], JsValue> InvokerFor(object instance, MethodInfo method)
    {
        var parameters = method.GetParameters();
        var engine = _engine;
        return args =>
        {
            var converted = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i < args.Length)
                {
                    var raw = args[i].ToObject();
                    if (raw is not null && !parameters[i].ParameterType.IsInstanceOfType(raw))
                    {
                        try { raw = Convert.ChangeType(raw, parameters[i].ParameterType, System.Globalization.CultureInfo.InvariantCulture); }
                        catch { /* let MethodInfo.Invoke's default binder make the final attempt */ }
                    }
                    converted[i] = raw;
                }
                else
                {
                    converted[i] = parameters[i].HasDefaultValue
                        ? parameters[i].DefaultValue
                        : parameters[i].ParameterType.IsValueType
                            ? Activator.CreateInstance(parameters[i].ParameterType)
                            : null;
                }
            }
            var result = method.Invoke(instance, converted);
            return result is null ? JsValue.Undefined : JsValue.FromObject(engine, result);
        };
    }

    // Public JS global `require(name)` — kept for backward compatibility. The
    // internal module-loading path no longer uses it (see BuildModule above).
    private JsValue Require(string value)
    {
        var inst = _moduleRegistry.BuildModuleInstance(value, _serviceProvider, this, _providedTypeFactories, _useTaggedModules);
        _instantiatedModules[inst.GetType()] = inst;
        return JsValue.FromObject(_engine, inst);
    }

    // --- Disposal ---

    public void Dispose()
    {
        foreach (var kvp in _timers)
        {
            if (_timers.TryRemove(kvp.Key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        _cancellationTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
