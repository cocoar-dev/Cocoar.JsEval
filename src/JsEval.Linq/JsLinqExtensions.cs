using System.Linq.Expressions;
using Cocoar.JsEval.Linq.Internal;
using Jint;
using Jint.Native;

namespace Cocoar.JsEval.Linq;

/// <summary>
/// Extension methods on <see cref="IQueryable{T}"/> with the natural LINQ names
/// (<c>Where</c>, <c>Find</c>, <c>Count</c>, <c>Any</c>) that accept JS lambdas.
/// Register with Jint via
/// <c>engine.Options.AddExtensionMethods(typeof(JsLinqExtensions))</c>, or use
/// <c>JsEvalBuilder.AddLinq()</c>.
/// </summary>
public static class JsLinqExtensions
{
    public static IQueryable<T> Where<T>(this IQueryable<T> source, JsValue predicate) =>
        source.Where(Translate<T>(predicate));

    public static T? Find<T>(this IQueryable<T> source, JsValue predicate) =>
        source.FirstOrDefault(Translate<T>(predicate));

    public static int Count<T>(this IQueryable<T> source, JsValue predicate) =>
        IsMissing(predicate) ? source.Count() : source.Count(Translate<T>(predicate));

    public static bool Any<T>(this IQueryable<T> source, JsValue predicate) =>
        IsMissing(predicate) ? source.Any() : source.Any(Translate<T>(predicate));

    // --- Ordering ---

    public static IOrderedQueryable<T> OrderBy<T>(this IQueryable<T> source, JsValue keySelector) =>
        (IOrderedQueryable<T>)InvokeOrderingMethod(nameof(Queryable.OrderBy), source, keySelector);

    public static IOrderedQueryable<T> OrderByDescending<T>(this IQueryable<T> source, JsValue keySelector) =>
        (IOrderedQueryable<T>)InvokeOrderingMethod(nameof(Queryable.OrderByDescending), source, keySelector);

    public static IOrderedQueryable<T> ThenBy<T>(this IOrderedQueryable<T> source, JsValue keySelector) =>
        (IOrderedQueryable<T>)InvokeOrderingMethod(nameof(Queryable.ThenBy), source, keySelector);

    public static IOrderedQueryable<T> ThenByDescending<T>(this IOrderedQueryable<T> source, JsValue keySelector) =>
        (IOrderedQueryable<T>)InvokeOrderingMethod(nameof(Queryable.ThenByDescending), source, keySelector);

    private static IQueryable InvokeOrderingMethod<T>(string methodName, IQueryable<T> source, JsValue keySelector)
    {
        var lambda = TranslateLambda<T>(keySelector);
        var method = ReflectionCache.GetQueryableClosed(methodName, typeof(T), lambda.ReturnType);
        return (IQueryable)method.Invoke(null, [source, lambda])!;
    }

    // --- helpers ---

    private static Expression<Func<T, bool>> Translate<T>(JsValue predicate) =>
        JsExpressionTranslator.Translate<T, bool>(predicate, JsLinqContext.CurrentEngine, JsLinqContext.CurrentOptions);

    private static LambdaExpression TranslateLambda<T>(JsValue keySelector) =>
        JsExpressionTranslator.TranslateLambda<T>(keySelector, JsLinqContext.CurrentEngine, JsLinqContext.CurrentOptions);

    private static bool IsMissing(JsValue v) => v.IsNull() || v.IsUndefined();
}
