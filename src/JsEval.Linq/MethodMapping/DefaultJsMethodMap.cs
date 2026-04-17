using System.Linq.Expressions;
using System.Reflection;

namespace Cocoar.JsEval.Linq.MethodMapping;

/// <summary>
/// Default map: JS camelCase → C# PascalCase for well-known string methods.
/// Leaves the rest to the translator's reflection-based resolution.
/// </summary>
public sealed class DefaultJsMethodMap : IJsMethodMap
{
    public bool TryResolve(MethodResolveRequest request, out Expression? result)
    {
        if (request.Target.Type == typeof(string))
        {
            var clrName = request.JsMethodName switch
            {
                "startsWith"  => nameof(string.StartsWith),
                "endsWith"    => nameof(string.EndsWith),
                "includes"    => nameof(string.Contains),
                "indexOf"     => nameof(string.IndexOf),
                "toLowerCase" => nameof(string.ToLower),
                "toUpperCase" => nameof(string.ToUpper),
                "trim"        => nameof(string.Trim),
                _ => null
            };
            if (clrName != null && TryCallInstance(request.Target, clrName, request.Arguments, out var call))
            {
                result = call;
                return true;
            }
        }
        result = null;
        return false;
    }

    private static bool TryCallInstance(Expression target, string name, IReadOnlyList<Expression> args, out MethodCallExpression? call)
    {
        var argTypes = args.Select(a => a.Type).ToArray();
        var method = target.Type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, binder: null, types: argTypes, modifiers: null);
        if (method == null) { call = null; return false; }
        call = Expression.Call(target, method, args);
        return true;
    }
}
