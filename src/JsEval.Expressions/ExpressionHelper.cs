using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Cocoar.JsEval.Expressions;

/// <summary>
/// General-purpose helpers for building LINQ-compatible Expression Trees.
/// Designed as building blocks for custom QueryBuilder implementations.
/// All property name parameters support dotted paths (e.g., "Customer.Name").
/// </summary>
public static class ExpressionHelper
{
    // --- Contains / IN ---

    /// <summary>
    /// Builds a <c>list.Contains(x.Property)</c> expression using the closure pattern.
    /// Works with any LINQ provider (Marten, EF Core, etc.).
    /// Supports dotted paths (e.g., "Customer.Id").
    /// </summary>
    public static Expression<Func<T, bool>> Contains<T, TValue>(string propertyPath, IEnumerable<TValue> values)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var body = BuildContainsExpression(property, values);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>
    /// Builds a <c>list.Contains(member)</c> expression for a given member expression.
    /// Uses the closure pattern (<see cref="ListHolder{T}"/>) for LINQ provider compatibility.
    /// </summary>
    public static Expression BuildContainsExpression<TValue>(MemberExpression member, IEnumerable<TValue> values)
    {
        var holder = ListHolder.Create(values);
        var holderExpr = holder.AsExpression();
        var containsMethod = typeof(List<TValue>).GetMethod(nameof(List<TValue>.Contains), [typeof(TValue)])!;
        return Expression.Call(holderExpr, containsMethod, member);
    }

    // --- Equality ---

    /// <summary>
    /// Builds <c>x.Property == value</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> Equal<T, TValue>(string propertyPath, TValue value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var constant = Expression.Constant(value, typeof(TValue));
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(property, constant), param);
    }

    /// <summary>
    /// Builds <c>x.Property != value</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> NotEqual<T, TValue>(string propertyPath, TValue value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var constant = Expression.Constant(value, typeof(TValue));
        return Expression.Lambda<Func<T, bool>>(Expression.NotEqual(property, constant), param);
    }

    // --- String Operations ---

    /// <summary>
    /// Builds <c>x.Property.StartsWith(value)</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> StartsWith<T>(string propertyPath, string value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var method = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        var constant = Expression.Constant(value, typeof(string));
        return Expression.Lambda<Func<T, bool>>(Expression.Call(property, method, constant), param);
    }

    /// <summary>
    /// Builds <c>x.Property.Contains(value)</c> (string contains). Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> StringContains<T>(string propertyPath, string value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var method = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        var constant = Expression.Constant(value, typeof(string));
        return Expression.Lambda<Func<T, bool>>(Expression.Call(property, method, constant), param);
    }

    /// <summary>
    /// Builds <c>x.Property.EndsWith(value)</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> EndsWith<T>(string propertyPath, string value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var method = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
        var constant = Expression.Constant(value, typeof(string));
        return Expression.Lambda<Func<T, bool>>(Expression.Call(property, method, constant), param);
    }

    // --- Comparison ---

    /// <summary>
    /// Builds <c>x.Property &gt; value</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> GreaterThan<T, TValue>(string propertyPath, TValue value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var constant = Expression.Constant(value, typeof(TValue));
        return Expression.Lambda<Func<T, bool>>(Expression.GreaterThan(property, constant), param);
    }

    /// <summary>
    /// Builds <c>x.Property &gt;= value</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> GreaterThanOrEqual<T, TValue>(string propertyPath, TValue value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var constant = Expression.Constant(value, typeof(TValue));
        return Expression.Lambda<Func<T, bool>>(Expression.GreaterThanOrEqual(property, constant), param);
    }

    /// <summary>
    /// Builds <c>x.Property &lt; value</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> LessThan<T, TValue>(string propertyPath, TValue value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var constant = Expression.Constant(value, typeof(TValue));
        return Expression.Lambda<Func<T, bool>>(Expression.LessThan(property, constant), param);
    }

    /// <summary>
    /// Builds <c>x.Property &lt;= value</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> LessThanOrEqual<T, TValue>(string propertyPath, TValue value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        var constant = Expression.Constant(value, typeof(TValue));
        return Expression.Lambda<Func<T, bool>>(Expression.LessThanOrEqual(property, constant), param);
    }

    // --- Null Checks ---

    /// <summary>
    /// Builds <c>x.Property == null</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> IsNull<T>(string propertyPath)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        return Expression.Lambda<Func<T, bool>>(
            Expression.Equal(property, Expression.Constant(null, property.Type)), param);
    }

    /// <summary>
    /// Builds <c>x.Property != null</c>. Supports dotted paths.
    /// </summary>
    public static Expression<Func<T, bool>> IsNotNull<T>(string propertyPath)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = PropertyPath.Resolve(param, propertyPath);
        return Expression.Lambda<Func<T, bool>>(
            Expression.NotEqual(property, Expression.Constant(null, property.Type)), param);
    }

    // --- Collection Navigation (Any / All) ---

    /// <summary>
    /// Builds <c>x.Collection.Any(item => item.Property == value)</c>.
    /// For collection navigation properties (e.g., "find todos where any responsible matches").
    ///
    /// <example>
    /// <code>
    /// var expr = ExpressionHelper.Any&lt;TodoView, ResponsibleView, Guid&gt;(
    ///     "Responsibles", "Id", userId);
    /// // → x => x.Responsibles.Any(r => r.Id == userId)
    /// </code>
    /// </example>
    /// </summary>
    public static Expression<Func<T, bool>> Any<T, TItem, TValue>(
        string collectionProperty, string itemProperty, TValue value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var collection = PropertyPath.Resolve(param, collectionProperty);

        // Build inner lambda: item => item.Property == value
        var itemParam = Expression.Parameter(typeof(TItem), "item");
        var itemProp = PropertyPath.Resolve(itemParam, itemProperty);
        var constant = Expression.Constant(value, typeof(TValue));
        var innerBody = Expression.Equal(itemProp, constant);
        var innerLambda = Expression.Lambda<Func<TItem, bool>>(innerBody, itemParam);

        // Call Enumerable.Any<TItem>(collection, predicate)
        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TItem));

        var body = Expression.Call(anyMethod, collection, innerLambda);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>
    /// Builds <c>x.Collection.Any(predicate)</c> with a custom predicate.
    /// </summary>
    public static Expression<Func<T, bool>> Any<T, TItem>(
        string collectionProperty, Expression<Func<TItem, bool>> predicate)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var collection = PropertyPath.Resolve(param, collectionProperty);

        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TItem));

        var body = Expression.Call(anyMethod, collection, predicate);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>
    /// Builds <c>x.Collection.Any()</c> (checks if collection has any items).
    /// </summary>
    public static Expression<Func<T, bool>> HasAny<T, TItem>(string collectionProperty)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var collection = PropertyPath.Resolve(param, collectionProperty);

        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(TItem));

        var body = Expression.Call(anyMethod, collection);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    // --- OrderBy ---

    /// <summary>
    /// Builds an order-by selector: <c>x => x.Property</c>.
    /// Supports dotted paths. Returns an untyped LambdaExpression
    /// suitable for dynamic OrderBy calls.
    ///
    /// <example>
    /// <code>
    /// var selector = ExpressionHelper.OrderBy&lt;TodoView&gt;("DueDate");
    /// // → (Expression&lt;Func&lt;TodoView, DateTime&gt;&gt;) x => x.DueDate
    ///
    /// var selector = ExpressionHelper.OrderBy&lt;TodoView&gt;("Customer.Name");
    /// // → (Expression&lt;Func&lt;TodoView, string&gt;&gt;) x => x.Customer.Name
    /// </code>
    /// </example>
    /// </summary>
    public static LambdaExpression OrderBy<T>(string propertyPath)
    {
        return PropertyPath.BuildSelector(typeof(T), propertyPath);
    }

    /// <summary>
    /// Builds a typed order-by selector: <c>Expression&lt;Func&lt;T, TKey&gt;&gt;</c>.
    /// </summary>
    public static Expression<Func<T, TKey>> OrderBy<T, TKey>(string propertyPath)
    {
        return PropertyPath.BuildSelector<T, TKey>(propertyPath);
    }

    // --- Combinators ---

    /// <summary>
    /// Combines two expressions with AND: <c>expr1 &amp;&amp; expr2</c>.
    /// </summary>
    public static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var param = left.Parameters[0];
        var rightBody = new ParameterReplacer(right.Parameters[0], param).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, rightBody), param);
    }

    /// <summary>
    /// Combines two expressions with OR: <c>expr1 || expr2</c>.
    /// </summary>
    public static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var param = left.Parameters[0];
        var rightBody = new ParameterReplacer(right.Parameters[0], param).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, rightBody), param);
    }

    /// <summary>
    /// Negates an expression: <c>!expr</c>.
    /// </summary>
    public static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> expression)
    {
        return Expression.Lambda<Func<T, bool>>(Expression.Not(expression.Body), expression.Parameters);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
