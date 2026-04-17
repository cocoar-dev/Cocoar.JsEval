using System.Linq.Expressions;

namespace Cocoar.JsEval.Linq.Building;

/// <summary>
/// Wraps a list in a class to simulate the C# closure pattern for Expression Trees.
///
/// LINQ providers (Marten, EF Core, ...) cannot translate a naked
/// <see cref="ConstantExpression"/> containing a list to SQL. They expect a
/// <see cref="MemberExpression"/> pointing to a field/property on a captured closure —
/// the same pattern the C# compiler generates for <c>list.Contains(x.Id)</c> inside a lambda.
/// </summary>
/// <example>
/// Instead of:
/// <code>Expression.Constant(myList)  // → ConstantExpression (fails with LINQ providers)</code>
/// Use:
/// <code>
/// var holder = ListHolder.Create(myList);
/// holder.AsExpression()  // → MemberExpression (works with any LINQ provider)
/// </code>
/// </example>
internal sealed class ListHolder<T>
{
    public List<T> Values { get; set; } = [];

    /// <summary>
    /// Returns a <see cref="MemberExpression"/> that points to <see cref="Values"/> on this instance.
    /// </summary>
    public MemberExpression AsExpression() =>
        Expression.Property(Expression.Constant(this), nameof(Values));
}

/// <summary>Factory for <see cref="ListHolder{T}"/>.</summary>
internal static class ListHolder
{
    public static ListHolder<T> Create<T>(IEnumerable<T> values) =>
        new() { Values = [.. values] };
}
