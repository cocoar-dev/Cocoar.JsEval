using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Jint;
using Jint.Runtime.Interop;
using Jint.Runtime.Debugger;

namespace Cocoar.JsEval.Engine;

public sealed class JsEngineOptions
{
    internal Options JintOptions { get; }
    public List<Type> AllowedExtensionMethods { get; } = [];

    public JsEngineOptions()
    {
        JintOptions = new Options()
            .CatchClrExceptions()
            .AllowOperatorOverloading()
            // Defense-in-depth defaults (4.0). Stop runaway scripts even when
            // the consumer doesn't ship its own wall-clock budget. Override
            // via WithExecutionTimeout / WithMaxStatements; pass
            // Timeout.InfiniteTimeSpan or 0 to disable.
            .TimeoutInterval(TimeSpan.FromSeconds(10))
            .MaxStatements(5_000_000);

        // Enable automatic .NET Task/ValueTask → JS Promise conversion.
        // Scripts can `await` .NET async methods directly.
        JintOptions.ExperimentalFeatures = ExperimentalFeature.TaskInterop;

        // Jint's LiveView array conversion (default since 4.14) is taken
        // deliberately, not by inheritance. Under the previous Copy mode a
        // script's writes to a host T[] all appeared to succeed and none of
        // them reached the CLR array — push(), sort(), reverse() and indexed
        // writes were silently discarded. A live view writes those through and
        // instead throws on the two operations a fixed-size CLR array cannot
        // honour, push() and a length assignment. ClrArrayInteropTests pins it.
    }

    /// <summary>
    /// Maximum wall-clock time a single script execution may run before Jint
    /// raises <see cref="Jint.Runtime.ExecutionCanceledException"/>. Default: <c>10 s</c>.
    /// Pass <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable.
    /// </summary>
    public JsEngineOptions WithExecutionTimeout(TimeSpan timeout)
    {
        JintOptions.TimeoutInterval(timeout);
        return this;
    }

    /// <summary>
    /// Maximum number of statements a single script execution may evaluate
    /// before Jint raises <see cref="Jint.Runtime.StatementsCountOverflowException"/>.
    /// Default: <c>5 000 000</c>. Pass <c>0</c> to disable.
    /// </summary>
    public JsEngineOptions WithMaxStatements(int maxStatements)
    {
        JintOptions.MaxStatements(maxStatements);
        return this;
    }

    /// <summary>
    /// Maximum nesting depth of a JavaScript value the engine will convert to
    /// JSON, for <see cref="JsEngine.GetValue{T}"/> and
    /// <see cref="JsEngine.JsonStringify"/>. Default: <c>512</c>.
    ///
    /// This is a safety limit, not a preference. JSON serialization recurses
    /// once per level, so a value a script nested a few thousand deep exhausts
    /// the .NET stack and terminates the process with an uncatchable
    /// <c>StackOverflowException</c>. The default sits far above any realistic
    /// payload and far below the crash threshold; converting a deeper value
    /// raises <see cref="InvalidOperationException"/> instead.
    /// </summary>
    public int MaxJsonDepth { get; private set; } = 512;

    /// <inheritdoc cref="MaxJsonDepth"/>
    public JsEngineOptions WithMaxJsonDepth(int maxDepth)
    {
        if (maxDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "MaxJsonDepth must be positive.");
        MaxJsonDepth = maxDepth;
        return this;
    }

    /// <summary>
    /// Configures the underlying Jint <see cref="Options"/> directly, before the
    /// engine is constructed. This is the escape hatch for Jint settings this
    /// builder does not surface; note that options taking effect at construction
    /// time — the interop <c>TypeResolver</c> among them — cannot be set from
    /// <see cref="RegisterEngineConfigurator"/>, which only sees the finished engine.
    /// </summary>
    public JsEngineOptions ConfigureJint(Action<Options> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(JintOptions);
        return this;
    }

    /// <summary>
    /// Restricts script access to the declared members. Anything not declared
    /// stops existing for the script, so passing an object no longer grants its
    /// whole reachable object graph.
    ///
    /// A denied member reads as <c>undefined</c>, the same as any member that
    /// does not exist. Jint can turn that into a <c>MissingMemberException</c>
    /// via <c>Interop.ThrowOnUnresolvedMember</c>, which makes denials obvious
    /// but is deliberately not switched on here: that setting also fires on the
    /// <c>toJSON</c> probe, so it breaks <c>JSON.stringify</c> for every wrapped
    /// CLR object. Enable it through <see cref="ConfigureJint"/> if loud denials
    /// matter more to you than serializing host objects.
    ///
    /// Members of types the engine never wraps — JS primitives such as the
    /// <see cref="string"/> a property returns, or a <see cref="DateTime"/> that
    /// crosses as a JS <c>Date</c> — are unaffected; the filter only governs CLR
    /// member resolution.
    /// </summary>
    public JsEngineOptions AllowOnly(Action<JsInteropAllowlist> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _allowlist ??= new JsInteropAllowlist();
        build(_allowlist);
        ApplyInteropRules();
        return this;
    }

    /// <summary>
    /// Refuses to expose instances of the given types and anything assignable to
    /// them — <c>DbContext</c>, <c>IServiceProvider</c>, <c>HttpClient</c> and
    /// similar. Two checks are installed: members whose declared type is denied
    /// disappear, and any value that turns out to be a denied type at runtime is
    /// rejected when it would be wrapped. The second check is what catches a
    /// member declared as <c>object</c> or an interface, where the declared type
    /// says nothing about what actually comes back.
    ///
    /// This is a safety net rather than the primary boundary: a deny list is only
    /// as complete as its author, whereas <see cref="AllowOnly"/> is closed by
    /// construction. It earns its keep by catching what someone allows by mistake.
    /// </summary>
    public JsEngineOptions DenyTypes(params Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        foreach (var type in types)
        {
            ArgumentNullException.ThrowIfNull(type);
            _deniedTypes.Add(type);
        }
        ApplyInteropRules();
        return this;
    }

    private JsInteropAllowlist? _allowlist;
    private readonly List<Type> _deniedTypes = [];

    private void ApplyInteropRules()
    {
        var allowlist = _allowlist;
        var denied = _deniedTypes;

        JintOptions.Interop.TypeResolver = new TypeResolver
        {
            MemberFilter = member =>
            {
                if (denied.Count > 0 && IsDenied(YieldedType(member), denied))
                    return false;

                if (allowlist is null)
                    return true;

                return allowlist.Members.Contains(member)
                    || (member.DeclaringType is not null && allowlist.Types.Contains(member.DeclaringType));
            }
        };

        if (denied.Count > 0)
        {
            JintOptions.Interop.WrapObjectHandler = (engine, target, type) =>
                target is not null && denied.Exists(d => d.IsInstanceOfType(target))
                    ? throw new InvalidOperationException(
                        $"Type '{target.GetType().FullName}' is denied and cannot be exposed to script.")
                    : ObjectWrapper.Create(engine, target!);
        }
    }

    private static Type? YieldedType(MemberInfo member) => member switch
    {
        PropertyInfo p => p.PropertyType,
        FieldInfo f => f.FieldType,
        MethodInfo m => m.ReturnType,
        _ => null
    };

    private static bool IsDenied(Type? type, List<Type> denied) =>
        type is not null && denied.Exists(d => d.IsAssignableFrom(type));

    private bool _sandboxed;

    /// <summary>
    /// Locks the engine into a hardened configuration for scripts the host does
    /// not control, and refuses any later call that would widen it again.
    ///
    /// Sets strict mode, disables <c>eval</c> and the <c>Function</c>
    /// constructor, fixes culture and time zone to invariant/UTC, applies
    /// memory, recursion, execution-stack, array and regex limits, tightens the
    /// execution timeout and statement cap, blocks <c>GetType()</c>, reflection
    /// and CLR assembly access, removes the shared-memory primitives, and
    /// freezes the built-in prototypes.
    ///
    /// CLR interop itself stays on — that is the point of this surface. Use
    /// <see cref="AllowOnly"/> to decide which members a script may reach and
    /// <see cref="DenyTypes"/> to rule out types outright; both remain available
    /// afterwards because they narrow rather than widen.
    ///
    /// Isolation between scripts is the engine instance: globals and prototype
    /// changes persist for the lifetime of one <see cref="JsEngine"/>, which is
    /// registered per DI scope. Running scripts from different sources in one
    /// scope shares that state — resolve a separate engine for each instead.
    /// </summary>
    public JsEngineOptions Sandboxed()
    {
        if (_widened.Count > 0)
            throw new InvalidOperationException(
                $"Sandboxed() cannot be applied after {string.Join(", ", _widened)} — " +
                "those grant capabilities a sandboxed engine must not have. Remove them or drop Sandboxed().");

        _sandboxed = true;

        JintOptions
            .Strict()
            .DisableStringCompilation()
            .Culture(CultureInfo.InvariantCulture)
            .LocalTimeZone(TimeZoneInfo.Utc)
            .TimeoutInterval(TimeSpan.FromSeconds(2))
            .MaxStatements(1_000_000)
            .LimitMemory(16 * 1024 * 1024)
            .LimitRecursion(64);

        JintOptions.Constraints.MaxArraySize = 100_000;
        JintOptions.Constraints.MaxExecutionStackCount = 256;
        JintOptions.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(500);
        JintOptions.Constraints.PromiseTimeout = TimeSpan.FromSeconds(2);
        JintOptions.Json.MaxParseDepth = MaxJsonDepth;

        JintOptions.Interop.AllowGetType = false;
        JintOptions.Interop.AllowSystemReflection = false;
        JintOptions.Interop.AllowedAssemblies.Clear();

        RegisterEngineConfigurator(static engine =>
        {
            // Shared-memory primitives can block or spin and are useless for rules.
            engine.SetValue("Atomics", Jint.Native.JsValue.Undefined);
            engine.SetValue("SharedArrayBuffer", Jint.Native.JsValue.Undefined);

            // JSON.stringify reads inherited index properties on sparse arrays,
            // so an unfrozen Array.prototype lets a script inject values into
            // its own result that it never assigned.
            engine.Execute("""
                for (const ctor of [Object, Array, String, Number, Boolean, Date, RegExp, Function, Error]) {
                    Object.freeze(ctor.prototype);
                }
                """);
        });

        return this;
    }

    private readonly List<string> _widened = [];

    /// <summary>
    /// Records a capability grant and refuses it once <see cref="Sandboxed"/>
    /// has been applied, so the guarantee cannot be undone by a later call and
    /// does not depend on the order the builder happens to be written in.
    /// </summary>
    private void Widening(string what)
    {
        if (_sandboxed)
            throw new InvalidOperationException(
                $"{what} cannot be enabled on a sandboxed engine. " +
                "Grant capabilities through modules or AllowOnly instead, or drop Sandboxed().");

        _widened.Add(what);
    }

    public JsEngineOptions EnableDebugMode()
    {
        JintOptions
            .DebugMode()
            .DebuggerStatementHandling(DebuggerStatementHandling.Script);
        return this;
    }

    public JsEngineOptions AddExtensionMethods<T>()
    {
        return AddExtensionMethods(typeof(T));
    }

    public JsEngineOptions AddExtensionMethods(params Type[] types)
    {
        JintOptions.AddExtensionMethods(types);
        AllowedExtensionMethods.AddRange(types);
        return this;
    }

    /// <summary>
    /// Register extension-method container types with Jint's runtime resolver
    /// ONLY — they are <b>not</b> surfaced in <c>TsDefinitionService</c>'s
    /// emitted <c>.d.ts</c> files. Use for BCL-wide types like
    /// <see cref="System.Linq.Enumerable"/> where we want JS-side method
    /// resolution (<c>[1,2,3].Where(...)</c>) but don't want 500+ Enumerable
    /// signatures polluting the generated <c>System.d.ts</c>.
    /// </summary>
    public JsEngineOptions AddRuntimeOnlyExtensionMethods(params Type[] types)
    {
        JintOptions.AddExtensionMethods(types);
        return this;
    }

    public JsEngineOptions AllowAssemblies(params Assembly[] assemblies)
    {
        Widening(nameof(AllowAssemblies));
        JintOptions.AllowClr(assemblies);
        return this;
    }

    public JsEngineOptions AllowCurrentDomainAssemblies()
    {
        Widening(nameof(AllowCurrentDomainAssemblies));
        return AllowAssemblies(AppDomain.CurrentDomain.GetAssemblies());
    }

    /// <summary>
    /// Enable the browser-compatible fetch() global function.
    /// Must be explicitly enabled — not available by default for sandboxing.
    /// </summary>
    internal bool FetchEnabled { get; private set; }

    public JsEngineOptions EnableFetch()
    {
        Widening(nameof(EnableFetch));
        FetchEnabled = true;
        return this;
    }

    /// <summary>
    /// Enable the <c>NewObject(typeName, args)</c> JS global. Off by default —
    /// when enabled, only types registered via <c>AddTypeAlias</c> resolve
    /// (alias-only). To allow assembly-walk fallback, additionally call
    /// <c>EnableNewObjectAssemblyFallback(...)</c> with an explicit allowlist.
    /// </summary>
    internal bool NewObjectEnabled { get; private set; }

    public JsEngineOptions EnableNewObject()
    {
        Widening(nameof(EnableNewObject));
        NewObjectEnabled = true;
        return this;
    }

    /// <summary>
    /// Allowlist of assemblies <c>NewObject(typeName)</c> may consult when no
    /// matching <see cref="TypeAliases"/> entry is found. Empty by default —
    /// in which case <c>NewObject</c> resolves alias-only and returns
    /// <c>null</c> for unknown names. Repeated calls accumulate assemblies.
    /// Independent of <see cref="NewObjectEnabled"/>: a populated allowlist
    /// has no effect unless <c>EnableNewObject()</c> is also called.
    /// </summary>
    internal List<Assembly> NewObjectAssemblyFallback { get; } = [];

    public JsEngineOptions EnableNewObjectAssemblyFallback(params Assembly[] assemblies)
    {
        Widening(nameof(EnableNewObjectAssemblyFallback));
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var asm in assemblies)
            if (asm is not null && !NewObjectAssemblyFallback.Contains(asm))
                NewObjectAssemblyFallback.Add(asm);
        return this;
    }

    /// <summary>
    /// Enable the <c>require(name)</c> JS global for runtime module loading.
    /// Off by default — modules are normally consumed via ES <c>import</c>.
    /// </summary>
    internal bool RequireEnabled { get; private set; }

    public JsEngineOptions EnableRequire()
    {
        Widening(nameof(EnableRequire));
        RequireEnabled = true;
        return this;
    }

    /// <summary>
    /// Enable the <c>setTimeout</c>/<c>setInterval</c>/<c>clearTimeout</c>/
    /// <c>clearInterval</c> JS globals. Off by default — fire-and-forget
    /// callbacks outlive the script and consume <see cref="System.Threading.Tasks.TaskScheduler"/>
    /// resources, which is rarely desirable for embedded sandbox scripts.
    /// </summary>
    internal bool TimersEnabled { get; private set; }

    public JsEngineOptions EnableTimers()
    {
        Widening(nameof(EnableTimers));
        TimersEnabled = true;
        return this;
    }

    /// <summary>
    /// Enable the <c>console.log/info/warn/error/debug</c> bridge to the
    /// host's <see cref="Microsoft.Extensions.Logging.ILogger"/>. Off by default —
    /// untrusted scripts can flood centralised log infrastructure (Serilog,
    /// ELK, Cloud Logging). Enable explicitly when you want script output
    /// to reach host logs.
    /// </summary>
    internal bool ConsoleEnabled { get; private set; }

    public JsEngineOptions EnableConsole()
    {
        Widening(nameof(EnableConsole));
        ConsoleEnabled = true;
        return this;
    }

    internal List<Action<Jint.Engine>> EngineConfigurators { get; } = [];

    /// <summary>
    /// Discriminator mappings for polymorphic types. When non-empty, <see cref="JsEngine"/>
    /// registers a <c>Type</c> global so scripts can call <c>Type.Is(a, 'dog')</c>.
    /// Populated by <c>JsEvalBuilder.AddDiscriminatorMappings</c>.
    /// </summary>
    public List<DiscriminatorMapping> DiscriminatorMappings { get; } = [];

    /// <summary>
    /// Registers a callback invoked after the underlying Jint engine is created,
    /// giving add-on packages a hook to register globals, extension scripts, or
    /// other setup without the core needing to know about them.
    /// </summary>
    public JsEngineOptions RegisterEngineConfigurator(Action<Jint.Engine> configurator)
    {
        EngineConfigurators.Add(configurator);
        return this;
    }

    /// <summary>
    /// Short-name aliases for types. Single source of truth consumed by:
    /// <list type="bullet">
    ///   <item><description><c>JsEngine</c>'s <c>NewObject(...)</c> resolver — <c>NewObject("CustomerView")</c> works</description></item>
    ///   <item><description><c>TsDefinitionService</c> — <c>.d.ts</c> renders the short name so Monaco hovers show the alias instead of the full namespace path</description></item>
    /// </list>
    /// Populated by <c>JsEvalBuilder.AddTypeAlias</c> and <c>JsEvalBuilder.MapNamespace</c>.
    /// </summary>
    public Dictionary<string, Type> TypeAliases { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Ordered list of (sourcePrefix, targetPrefix) namespace mappings. Applied
    /// in insertion order. When a matched type lands on an already-taken alias
    /// derived from another type, collision detection throws with both types
    /// listed.
    /// </summary>
    public List<(string Source, string Target)> NamespaceMappings { get; } = new();

    /// <summary>
    /// Register an explicit short-name alias for a type. Used by both
    /// <c>NewObject(...)</c> at runtime and the <c>.d.ts</c> renderer at save time.
    /// Throws if the alias is already assigned to a different type.
    /// </summary>
    public JsEngineOptions AddTypeAlias(Type type, string alias)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias must be a non-empty identifier.", nameof(alias));

        var resolved = type.IsGenericType && !type.IsGenericTypeDefinition
            ? type.GetGenericTypeDefinition()
            : type;

        if (TypeAliases.TryGetValue(alias, out var existing) && existing != resolved)
        {
            throw new InvalidOperationException(
                $"Type alias '{alias}' is already assigned to '{existing.FullName}'. " +
                $"Cannot reassign it to '{resolved.FullName}'. " +
                $"Pick a different alias to disambiguate.");
        }
        TypeAliases[alias] = resolved;
        return this;
    }

    /// <summary>
    /// Map a source namespace prefix to a target namespace prefix. Any type whose
    /// namespace starts with <paramref name="sourcePrefix"/> has that prefix
    /// replaced by <paramref name="targetPrefix"/> when rendering the <c>.d.ts</c>
    /// and when resolving <c>NewObject(name)</c>. Empty <paramref name="targetPrefix"/>
    /// flattens matched types to the root scope (short names).
    /// <para>
    /// <c>System.*</c> types are excluded by default. Collisions where two distinct
    /// types would end up with the same resolved name surface as
    /// <see cref="InvalidOperationException"/> at resolution time (render or
    /// NewObject map build) — not silently overridden.
    /// </para>
    /// </summary>
    public JsEngineOptions MapNamespace(string sourcePrefix, string targetPrefix)
    {
        ArgumentNullException.ThrowIfNull(sourcePrefix);
        ArgumentNullException.ThrowIfNull(targetPrefix);
        NamespaceMappings.Add((sourcePrefix, targetPrefix));
        return this;
    }
}
