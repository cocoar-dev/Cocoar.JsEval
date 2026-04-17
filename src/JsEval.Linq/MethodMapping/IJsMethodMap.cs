using System.Linq.Expressions;

namespace Cocoar.JsEval.Linq.MethodMapping;

/// <summary>
/// Strategy for resolving a JS method call against a CLR target type to a
/// <see cref="MethodCallExpression"/>. Extension point: consumers can supply
/// their own map to customize how JS methods are translated.
/// </summary>
public interface IJsMethodMap
{
    /// <summary>
    /// Attempts to build a <see cref="MethodCallExpression"/> for the given JS call.
    /// Return <c>false</c> to let the default reflection-based resolution take over.
    /// </summary>
    bool TryResolve(MethodResolveRequest request, out Expression? result);
}

/// <summary>Input to <see cref="IJsMethodMap.TryResolve"/>.</summary>
public sealed class MethodResolveRequest
{
    public Expression Target { get; init; } = default!;
    public string JsMethodName { get; init; } = default!;
    public IReadOnlyList<Expression> Arguments { get; init; } = Array.Empty<Expression>();
    public Func<Expression, Expression> TranslateSubExpression { get; init; } = default!;
}
