using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
            Tags = moduleAttribute?.Tags?.Select(t => t.ToLowerInvariant()).ToList() ?? []
        };

        RegisteredModules.TryAdd(name, moduleDefinition);

        _registered.Add(moduleType.FullName!);
    }

    public IEnumerable<IJsModuleDefinition> GetRegisteredModuleDefinitions()
    {
        return RegisteredModules.Values;
    }

    private static string TrimEnd(string value, string trim)
    {
        if (value.EndsWith(trim, StringComparison.Ordinal))
        {
            value = value[..^trim.Length];
        }

        return value;
    }
}
