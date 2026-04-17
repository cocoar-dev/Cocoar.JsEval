using System.Linq.Expressions;

namespace Cocoar.JsEval.Linq.MethodMapping;

/// <summary>
/// Tries each inner map in order; first non-false wins.
/// Lets consumers layer custom maps in front of the default.
/// </summary>
public sealed class CompositeJsMethodMap : IJsMethodMap
{
    private readonly IJsMethodMap[] _maps;

    public CompositeJsMethodMap(params IJsMethodMap[] maps) => _maps = maps;

    public bool TryResolve(MethodResolveRequest request, out Expression? result)
    {
        foreach (var map in _maps)
            if (map.TryResolve(request, out result))
                return true;
        result = null;
        return false;
    }
}
