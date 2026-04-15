using System.Collections.Generic;

namespace Cocoar.JsEval.Expressions;

/// <summary>
/// Wraps a list in a class to simulate the C# closure pattern for Expression Trees.
///
/// LINQ providers (Marten, EF Core) cannot translate a naked
/// <c>Expression.Constant(list)</c> to SQL. They expect a <c>MemberExpression</c>
/// pointing to a field/property on a captured closure — the same pattern the C# compiler
/// generates for <c>list.Contains(x.Id)</c> in a lambda.
///
/// <example>
/// Instead of:
/// <code>Expression.Constant(myList)  // → ConstantExpression (fails with LINQ providers)</code>
/// Use:
/// <code>
/// var holder = ListHolder.Create(myList);
/// holder.AsExpression()  // → MemberExpression (works with any LINQ provider)
/// </code>
/// </example>
/// </summary>
public sealed class ListHolder<T>
{
    public List<T> Values { get; set; } = [];

    /// <summary>
    /// Returns a MemberExpression that points to <see cref="Values"/> on this instance.
    /// This expression can be used in LINQ Where clauses that get translated to SQL.
    /// </summary>
    public System.Linq.Expressions.MemberExpression AsExpression() =>
        System.Linq.Expressions.Expression.Property(
            System.Linq.Expressions.Expression.Constant(this),
            nameof(Values));
}

/// <summary>
/// Factory for <see cref="ListHolder{T}"/>.
/// </summary>
public static class ListHolder
{
    public static ListHolder<T> Create<T>(IEnumerable<T> values) =>
        new() { Values = [.. values] };
}
