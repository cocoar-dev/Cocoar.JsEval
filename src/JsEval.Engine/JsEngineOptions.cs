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
}
