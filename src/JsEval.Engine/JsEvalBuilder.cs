using System;
using System.Collections.Generic;
using System.Reflection;
using Cocoar.JsEval;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Fluent builder for configuring JsEval engine options and module registration.
/// </summary>
public sealed class JsEvalBuilder
{
    internal JsEngineOptions Options { get; } = new();
    internal JsModuleRegistry ModuleRegistry { get; } = new();

    /// <summary>
    /// Deferred DI-side registrations applied by <c>ServiceCollectionExtensions.AddJsEval</c>
    /// after the builder callback runs. Used by add-on packages (e.g. <c>AddLinq</c>)
    /// that need to register services (like <see cref="IJsTsDefinitionContributor"/>s)
    /// alongside their engine-configurator hook.
    /// </summary>
    internal List<Action<IServiceCollection>> DeferredRegistrations { get; } = new();

    /// <summary>
    /// Registers a type that implements <see cref="IJsTsDefinitionContributor"/> as
    /// a singleton when the host calls <c>services.AddJsEval(b =&gt; ...)</c>.
    /// Used by add-on packages to contribute <c>.d.ts</c> files to
    /// <c>TsDefinitionService.GetTsDefinitions()</c> without being registered as
    /// a <see cref="IJsModule"/>.
    /// </summary>
    public JsEvalBuilder AddTsDefinitionContributor<T>() where T : class, IJsTsDefinitionContributor
    {
        DeferredRegistrations.Add(sc => sc.AddSingleton<IJsTsDefinitionContributor, T>());
        return this;
    }

    public JsEvalBuilder EnableFetch()
    {
        Options.EnableFetch();
        return this;
    }

    /// <summary>
    /// Registers a callback invoked after the underlying Jint engine is created.
    /// Used by add-on packages to register their globals.
    /// </summary>
    public JsEvalBuilder RegisterEngineConfigurator(Action<Jint.Engine> configurator)
    {
        Options.RegisterEngineConfigurator(configurator);
        return this;
    }

    /// <summary>
    /// Exposes <see cref="CsDateTime"/> as a JS global so scripts can do date
    /// arithmetic from JS — <c>CsDateTime.Now.AddDays(7)</c> — without Jint
    /// flattening the value into a JS <c>Date</c>.
    /// </summary>
    public JsEvalBuilder EnableCsDateTime()
    {
        Options.RegisterEngineConfigurator(CsDateTimeGlobals.Register);
        return this;
    }

    public JsEvalBuilder EnableDebugMode()
    {
        Options.EnableDebugMode();
        return this;
    }

    public JsEvalBuilder AddModule<T>() where T : IJsModule
    {
        ModuleRegistry.RegisterModule<T>();
        return this;
    }

    public JsEvalBuilder AddExtensionMethods<T>()
    {
        Options.AddExtensionMethods<T>();
        return this;
    }

    public JsEvalBuilder AddExtensionMethods(params Type[] types)
    {
        Options.AddExtensionMethods(types);
        return this;
    }

    public JsEvalBuilder AllowAssemblies(params Assembly[] assemblies)
    {
        Options.AllowAssemblies(assemblies);
        return this;
    }

    public JsEvalBuilder AllowCurrentDomainAssemblies()
    {
        Options.AllowCurrentDomainAssemblies();
        return this;
    }

    /// <summary>
    /// Register an explicit short-name alias for a type. Usable immediately in
    /// scripts as <c>NewObject("Alias")</c>, and emitted as the short name in
    /// <c>.d.ts</c> output so Monaco hovers show the alias rather than the full
    /// namespace path.
    /// Throws if the alias is already assigned to a different type.
    /// </summary>
    public JsEvalBuilder AddTypeAlias<T>(string alias) => AddTypeAlias(typeof(T), alias);

    /// <inheritdoc cref="AddTypeAlias{T}(string)"/>
    public JsEvalBuilder AddTypeAlias<T>() => AddTypeAlias(typeof(T), typeof(T).Name);

    /// <inheritdoc cref="AddTypeAlias{T}(string)"/>
    public JsEvalBuilder AddTypeAlias(Type type, string alias)
    {
        Options.AddTypeAlias(type, alias);
        return this;
    }

    /// <summary>
    /// Map a source namespace prefix to a target namespace prefix for both
    /// <c>.d.ts</c> emission and <c>NewObject(...)</c> resolution. See
    /// <see cref="JsEngineOptions.MapNamespace(string, string)"/> for details.
    /// </summary>
    public JsEvalBuilder MapNamespace(string sourcePrefix, string targetPrefix)
    {
        Options.MapNamespace(sourcePrefix, targetPrefix);
        return this;
    }
}
