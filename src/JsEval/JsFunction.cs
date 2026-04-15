using System;
using System.Collections.Generic;

namespace Cocoar.JsEval;

public class JsFunction(string name, IReadOnlyList<string> parameterNames, Func<string, object[], object> invoker)
{
    public string Name { get; } = name;
    public IReadOnlyList<string> ParameterNames { get; } = parameterNames;

    public object Invoke(params object[] parameters) => invoker(Name, parameters);
}
