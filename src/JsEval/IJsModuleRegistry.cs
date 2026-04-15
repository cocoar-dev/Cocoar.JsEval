using System;
using System.Collections.Generic;

namespace Cocoar.JsEval;

public interface IJsModuleRegistry
{
    IJsModule BuildModuleInstance(string name, IServiceProvider serviceProvider,
        IScriptEngine currentScriptEngine, Dictionary<Type, Func<object>>? instanceDictionary = null,
        List<string>? useTaggedModules = null);

    IJsModule BuildSingleModuleInstance(IJsModuleDefinition module, IServiceProvider serviceProvider,
        IScriptEngine currentScriptEngine, Dictionary<Type, Func<object>>? instanceDictionary = null);

    IEnumerable<IJsModuleDefinition> GetRegisteredModuleDefinitions();
}
