using System;
using System.Collections.Generic;

namespace Cocoar.JsEval;

public interface IJsModuleRegistry
{
    IJsModule BuildModuleInstance(string name, IServiceProvider serviceProvider,
        IScriptEngine currentScriptEngine, Dictionary<Type, Func<object>>? instanceDictionary = null,
        List<string>? useTaggedModules = null);

#pragma warning disable CA1716 // Identifier conflicts with keyword 'Module' — public API, cannot rename
    IJsModule BuildSingleModuleInstance(IJsModuleDefinition module, IServiceProvider serviceProvider,
        IScriptEngine currentScriptEngine, Dictionary<Type, Func<object>>? instanceDictionary = null);
#pragma warning restore CA1716

    IEnumerable<IJsModuleDefinition> GetRegisteredModuleDefinitions();
}
