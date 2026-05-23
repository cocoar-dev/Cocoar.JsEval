using System;
using System.Collections.Generic;

namespace Cocoar.JsEval;

/// <summary>
/// Activates registered <see cref="IJsModule"/> instances for a given
/// <see cref="IScriptEngine"/>. Encapsulates the <see cref="IServiceProvider"/>
/// service-locator pattern that module construction requires (modules can
/// declare arbitrary constructor parameters that resolve from the host's DI
/// container), so <see cref="IScriptEngine"/> implementations themselves stay
/// constructor-pure — Wolverine codegen, AOT static analysis, and similar
/// tools see only typed dependencies on the engine.
/// </summary>
public interface IJsModuleBuilder
{
    /// <summary>
    /// Builds the module registered under <paramref name="name"/>. Respects
    /// the <paramref name="useTaggedModules"/> filter — modules with tags must
    /// have at least one tag present in this list; untagged modules always pass.
    /// </summary>
    IJsModule BuildModuleInstance(string name, IScriptEngine currentScriptEngine,
        Dictionary<Type, Func<object>>? instanceDictionary = null,
        List<string>? useTaggedModules = null);

#pragma warning disable CA1716 // Identifier conflicts with keyword 'Module' — public API, cannot rename
    /// <summary>
    /// Builds a specific module definition directly, bypassing the name lookup
    /// and tag filter. Used by tooling that already holds a module definition
    /// (e.g. <c>.d.ts</c> generation).
    /// </summary>
    IJsModule BuildSingleModuleInstance(IJsModuleDefinition module, IScriptEngine currentScriptEngine,
        Dictionary<Type, Func<object>>? instanceDictionary = null);
#pragma warning restore CA1716
}
