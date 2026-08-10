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

    // --- Asynchronous terminals ---
    //
    // A JavaScript expression has to produce a value, so the synchronous
    // terminals above execute the query then and there. Marten 9 refuses that:
    // it permits asynchronous data access only. These return a Task, which the
    // engine surfaces to the script as a promise, so a rule writes
    // `await users.countAsync(...)` exactly as C# writes `await CountAsync()`.
    //
    // The provider's own async terminal is located at runtime; providers without
    // one fall back to the synchronous call, so behaviour is unchanged there.

    // Every one of these takes the JsValue predicate, with no parameterless
    // overload — deliberately. A parameterless `CountAsync<T>(IQueryable<T>)`
    // here would be a *better* overload than the provider's own
    // `CountAsync<T>(IQueryable<T>, CancellationToken = default)`, so ordinary
    // C# in a file that has both `using Cocoar.JsEval.Linq` and `using Marten`
    // would silently bind to this one instead of Marten's. Pass `null` from a
    // script to mean "no predicate": `users.countAsync(null)`.

    /// <inheritdoc cref="Count{T}(IQueryable{T}, JsValue)"/>
    public static Task<int> CountAsync<T>(this IQueryable<T> source, JsValue predicate) =>
        AsyncQueryableBridge.InvokeAsync(Filtered(source, predicate), "CountAsync", static q => q.Count());

    /// <inheritdoc cref="Any{T}(IQueryable{T}, JsValue)"/>
    public static Task<bool> AnyAsync<T>(this IQueryable<T> source, JsValue predicate) =>
        AsyncQueryableBridge.InvokeAsync(Filtered(source, predicate), "AnyAsync", static q => q.Any());

    /// <inheritdoc cref="Find{T}(IQueryable{T}, JsValue)"/>
    public static Task<T?> FindAsync<T>(this IQueryable<T> source, JsValue predicate) =>
        AsyncQueryableBridge.InvokeAsync(
            Filtered(source, predicate), "FirstOrDefaultAsync", static q => q.FirstOrDefault());

    private static IQueryable<T> Filtered<T>(IQueryable<T> source, JsValue predicate) =>
        IsMissing(predicate) ? source : source.Where(Translate<T>(predicate));

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
