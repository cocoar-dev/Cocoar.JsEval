using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.JsEval;

public sealed class JsModuleRegistry : IJsModuleRegistry
{
    // NOTE: Since modules are only registered at startup and then read-only, a FrozenDictionary
    // could be used here for optimal read performance. However, that would require a "freeze" step
    // after registration completes, which would need API changes. ConcurrentDictionary is fine for reads.
    private ConcurrentDictionary<string, IJsModuleDefinition> RegisteredModules { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _registered = [];

    public JsModuleRegistry RegisterModule<T>() where T : IJsModule
    {
        RegisterModule(typeof(T));
        return this;
    }

    public void RegisterModule(Type moduleType)
    {
        if (_registered.Contains(moduleType.FullName!))
            return;

        var moduleAttribute = moduleType.GetCustomAttribute<JsModuleAttribute>();

        var name = (moduleAttribute?.Name ?? TrimEnd(moduleType.Name, "Module")).ToLowerInvariant();

        var moduleDefinition = new JsModuleDefinition(name, moduleType)
        {
            Tags = moduleAttribute?.Tags?.Select(t => t.ToLower()).ToList() ?? []
        };

        RegisteredModules.TryAdd(name, moduleDefinition);

        _registered.Add(moduleType.FullName!);
    }

    public IJsModule BuildModuleInstance(string name, IServiceProvider serviceProvider,
        IScriptEngine currentScriptEngine, Dictionary<Type, Func<object>>? instanceDictionary = null,
        List<string>? useTaggedModules = null)
    {
        if (!RegisteredModules.TryGetValue(name, out var module))
        {
            throw new KeyNotFoundException($"JsModule '{name}' is not registered.");
        }

        if (useTaggedModules is not null)
        {
            if (module.Tags?.Any() == true)
            {
                var hasAllowedTag = useTaggedModules.Any(tm => module.Tags.Contains(tm.ToLower()));
                if (!hasAllowedTag)
                {
                    throw new InvalidOperationException($"Module '{name}' is not available in this scripting context.");
                }
            }
        }

        return BuildSingleModuleInstance(module, serviceProvider, currentScriptEngine, instanceDictionary);
    }

    public IJsModule BuildSingleModuleInstance(IJsModuleDefinition module, IServiceProvider serviceProvider,
        IScriptEngine currentScriptEngine, Dictionary<Type, Func<object>>? instanceDictionary = null)
    {
        var constructor = module.ModuleType.GetConstructors().FirstOrDefault();
        if (constructor is null)
        {
            return (IJsModule)ActivatorUtilities.CreateInstance(serviceProvider, module.ModuleType);
        }

        var parameterInfos = constructor.GetParameters();
        if (parameterInfos.Length == 0)
        {
            return (IJsModule)ActivatorUtilities.CreateInstance(serviceProvider, module.ModuleType);
        }
        else
        {
            instanceDictionary ??= [];
            instanceDictionary[typeof(IScriptEngine)] = () => currentScriptEngine;

            return (IJsModule)Activator.CreateInstance(module.ModuleType, BuildConstructorParameters(parameterInfos, serviceProvider, instanceDictionary))!;
        }
    }

    public IEnumerable<IJsModuleDefinition> GetRegisteredModuleDefinitions()
    {
        return RegisteredModules.Values;
    }

    private static object[] BuildConstructorParameters(ParameterInfo[] parameterInfos, IServiceProvider serviceProvider, Dictionary<Type, Func<object>> instances)
    {
        var parameterInstances = new List<object>();
        foreach (var parameterInfo in parameterInfos)
        {
            if (instances.TryGetValue(parameterInfo.ParameterType, out var instance))
            {
                parameterInstances.Add(instance());
            }
            else
            {
                parameterInstances.Add(serviceProvider.GetService(parameterInfo.ParameterType)!);
            }
        }

        return [.. parameterInstances];
    }

    private static string TrimEnd(string value, string trim)
    {
        if (value.EndsWith(trim))
        {
            value = value[..^trim.Length];
        }

        return value;
    }
}
