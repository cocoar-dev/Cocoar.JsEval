using System;
using System.Reflection;
using Cocoar.JsEval;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Fluent builder for configuring JsEval engine options and module registration.
/// </summary>
public sealed class JsEvalBuilder
{
    internal JsEngineOptions Options { get; } = new();
    internal JsModuleRegistry ModuleRegistry { get; } = new();

    public JsEvalBuilder EnableFetch()
    {
        Options.EnableFetch();
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
}
