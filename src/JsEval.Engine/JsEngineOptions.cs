using System;
using System.Collections.Generic;
using System.Reflection;
using Jint;
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
        JintOptions.AllowClr(assemblies);
        return this;
    }

    public JsEngineOptions AllowCurrentDomainAssemblies()
    {
        return AllowAssemblies(AppDomain.CurrentDomain.GetAssemblies());
    }

    /// <summary>
    /// Enable the browser-compatible fetch() global function.
    /// Must be explicitly enabled — not available by default for sandboxing.
    /// </summary>
    internal bool FetchEnabled { get; private set; }

    public JsEngineOptions EnableFetch()
    {
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
