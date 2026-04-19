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
            .AllowOperatorOverloading();

        // Enable automatic .NET Task/ValueTask → JS Promise conversion.
        // Scripts can `await` .NET async methods directly.
        JintOptions.ExperimentalFeatures = ExperimentalFeature.TaskInterop;
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

    internal List<Action<Jint.Engine>> EngineConfigurators { get; } = [];

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
