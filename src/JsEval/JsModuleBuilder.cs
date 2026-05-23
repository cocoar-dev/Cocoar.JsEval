using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.JsEval;

/// <summary>
/// Default <see cref="IJsModuleBuilder"/> implementation. Holds the
/// <see cref="IServiceProvider"/> needed to resolve module-constructor
/// parameters; keeps that locator pattern off the engine's public ctor so
/// downstream codegen (Wolverine, etc.) sees only typed dependencies on the
/// engine itself.
/// </summary>
public sealed class JsModuleBuilder : IJsModuleBuilder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IJsModuleRegistry _registry;

    public JsModuleBuilder(IServiceProvider serviceProvider, IJsModuleRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
    }

    public IJsModule BuildModuleInstance(string name, IScriptEngine currentScriptEngine,
        Dictionary<Type, Func<object>>? instanceDictionary = null,
        List<string>? useTaggedModules = null)
    {
        var module = _registry.GetRegisteredModuleDefinitions()
            .FirstOrDefault(md => string.Equals(md.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"JsModule '{name}' is not registered.");

        if (useTaggedModules is not null && module.Tags?.Any() == true)
        {
            var hasAllowedTag = useTaggedModules.Any(tm => module.Tags.Contains(tm.ToLowerInvariant()));
            if (!hasAllowedTag)
                throw new InvalidOperationException($"Module '{name}' is not available in this scripting context.");
        }

        return BuildSingleModuleInstance(module, currentScriptEngine, instanceDictionary);
    }

    public IJsModule BuildSingleModuleInstance(IJsModuleDefinition module, IScriptEngine currentScriptEngine,
        Dictionary<Type, Func<object>>? instanceDictionary = null)
    {
        var constructor = module.ModuleType.GetConstructors().FirstOrDefault();
        if (constructor is null)
            return (IJsModule)ActivatorUtilities.CreateInstance(_serviceProvider, module.ModuleType);

        var parameterInfos = constructor.GetParameters();
        if (parameterInfos.Length == 0)
            return (IJsModule)ActivatorUtilities.CreateInstance(_serviceProvider, module.ModuleType);

        instanceDictionary ??= [];
        instanceDictionary[typeof(IScriptEngine)] = () => currentScriptEngine;

        return (IJsModule)Activator.CreateInstance(
            module.ModuleType,
            BuildConstructorParameters(parameterInfos, instanceDictionary))!;
    }

    private object[] BuildConstructorParameters(ParameterInfo[] parameterInfos, Dictionary<Type, Func<object>> instances)
    {
        var parameterInstances = new object[parameterInfos.Length];
        for (var i = 0; i < parameterInfos.Length; i++)
        {
            var p = parameterInfos[i];
            parameterInstances[i] = instances.TryGetValue(p.ParameterType, out var instance)
                ? instance()
                : _serviceProvider.GetService(p.ParameterType)!;
        }

        return parameterInstances;
    }
}
