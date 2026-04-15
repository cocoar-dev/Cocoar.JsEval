using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Cocoar.JsEval.Expressions;

/// <summary>
/// Fluent builder for collecting and combining filter expressions.
/// Supports conditional filters (<see cref="WhereIf"/>), AND/OR combination,
/// and all operators from <see cref="ExpressionHelper"/>.
///
/// <example>
/// <code>
/// var filter = new FilterBuilder&lt;TodoView&gt;()
///     .Where("IsArchived", false)
///     .WhereIf(hasCustomers, b =&gt; b.Contains("CustomerId", customerIds))
///     .WhereAny&lt;ResponsibleView, Guid&gt;("Responsibles", "Id", userId)
///     .Build();
///
/// var results = session.Query&lt;TodoView&gt;().Where(filter).ToListAsync();
/// </code>
/// </example>
/// </summary>
public sealed class FilterBuilder<T>
{
    private readonly List<Expression<Func<T, bool>>> _filters = [];

    // --- Equality ---

    /// <summary>
    /// Adds <c>x.Property == value</c>. Supports dotted paths.
    /// </summary>
    public FilterBuilder<T> Where<TValue>(string propertyPath, TValue value)
    {
        _filters.Add(ExpressionHelper.Equal<T, TValue>(propertyPath, value));
        return this;
    }

    /// <summary>
    /// Adds <c>x.Property != value</c>. Supports dotted paths.
    /// </summary>
    public FilterBuilder<T> WhereNot<TValue>(string propertyPath, TValue value)
    {
        _filters.Add(ExpressionHelper.NotEqual<T, TValue>(propertyPath, value));
        return this;
    }

    // --- Conditional ---

    /// <summary>
    /// Adds a filter only if <paramref name="condition"/> is true.
    /// The filter is built via a callback that receives this builder's API.
    ///
    /// <example>
    /// <code>
    /// .WhereIf(customerIds.Count > 0, b => b.Contains("CustomerId", customerIds))
    /// </code>
    /// </example>
    /// </summary>
    public FilterBuilder<T> WhereIf(bool condition, Action<FilterBuilder<T>> configure)
    {
        if (condition)
            configure(this);
        return this;
    }

    /// <summary>
    /// Adds a raw expression only if <paramref name="condition"/> is true.
    /// </summary>
    public FilterBuilder<T> WhereIf(bool condition, Expression<Func<T, bool>> filter)
    {
        if (condition)
            _filters.Add(filter);
        return this;
    }

    // --- Contains / IN ---

    /// <summary>
    /// Adds <c>list.Contains(x.Property)</c> using the closure pattern.
    /// </summary>
    public FilterBuilder<T> Contains<TValue>(string propertyPath, IEnumerable<TValue> values)
    {
        _filters.Add(ExpressionHelper.Contains<T, TValue>(propertyPath, values));
        return this;
    }

    // --- String Operations ---

    /// <summary>
    /// Adds <c>x.Property.StartsWith(value)</c>.
    /// </summary>
    public FilterBuilder<T> WhereStartsWith(string propertyPath, string value)
    {
        _filters.Add(ExpressionHelper.StartsWith<T>(propertyPath, value));
        return this;
    }

    /// <summary>
    /// Adds <c>x.Property.Contains(value)</c> (string contains).
    /// </summary>
    public FilterBuilder<T> WhereContains(string propertyPath, string value)
    {
        _filters.Add(ExpressionHelper.StringContains<T>(propertyPath, value));
        return this;
    }

    /// <summary>
    /// Adds <c>x.Property.EndsWith(value)</c>.
    /// </summary>
    public FilterBuilder<T> WhereEndsWith(string propertyPath, string value)
    {
        _filters.Add(ExpressionHelper.EndsWith<T>(propertyPath, value));
        return this;
    }

    // --- Comparison ---

    /// <summary>
    /// Adds <c>x.Property &gt; value</c>.
    /// </summary>
    public FilterBuilder<T> WhereGreaterThan<TValue>(string propertyPath, TValue value)
    {
        _filters.Add(ExpressionHelper.GreaterThan<T, TValue>(propertyPath, value));
        return this;
    }

    /// <summary>
    /// Adds <c>x.Property &gt;= value</c>.
    /// </summary>
    public FilterBuilder<T> WhereGreaterThanOrEqual<TValue>(string propertyPath, TValue value)
    {
        _filters.Add(ExpressionHelper.GreaterThanOrEqual<T, TValue>(propertyPath, value));
        return this;
    }

    /// <summary>
    /// Adds <c>x.Property &lt; value</c>.
    /// </summary>
    public FilterBuilder<T> WhereLessThan<TValue>(string propertyPath, TValue value)
    {
        _filters.Add(ExpressionHelper.LessThan<T, TValue>(propertyPath, value));
        return this;
    }

    /// <summary>
    /// Adds <c>x.Property &lt;= value</c>.
    /// </summary>
    public FilterBuilder<T> WhereLessThanOrEqual<TValue>(string propertyPath, TValue value)
    {
        _filters.Add(ExpressionHelper.LessThanOrEqual<T, TValue>(propertyPath, value));
        return this;
    }

    // --- Null Checks ---

    /// <summary>
    /// Adds <c>x.Property == null</c>.
    /// </summary>
    public FilterBuilder<T> WhereNull(string propertyPath)
    {
        _filters.Add(ExpressionHelper.IsNull<T>(propertyPath));
        return this;
    }

    /// <summary>
    /// Adds <c>x.Property != null</c>.
    /// </summary>
    public FilterBuilder<T> WhereNotNull(string propertyPath)
    {
        _filters.Add(ExpressionHelper.IsNotNull<T>(propertyPath));
        return this;
    }

    // --- Collection Navigation ---

    /// <summary>
    /// Adds <c>x.Collection.Any(item => item.Property == value)</c>.
    /// </summary>
    public FilterBuilder<T> WhereAny<TItem, TValue>(string collectionProperty, string itemProperty, TValue value)
    {
        _filters.Add(ExpressionHelper.Any<T, TItem, TValue>(collectionProperty, itemProperty, value));
        return this;
    }

    /// <summary>
    /// Adds <c>x.Collection.Any(predicate)</c> with a custom predicate.
    /// </summary>
    public FilterBuilder<T> WhereAny<TItem>(string collectionProperty, Expression<Func<TItem, bool>> predicate)
    {
        _filters.Add(ExpressionHelper.Any<T, TItem>(collectionProperty, predicate));
        return this;
    }

    // --- Enum ---

    /// <summary>
    /// Adds an enum equality using the string comparison strategy.
    /// </summary>
    public FilterBuilder<T> WhereEnumAsString(string propertyPath, string value)
    {
        _filters.Add(EnumExpressionHelper.EqualsAsString<T>(propertyPath, value));
        return this;
    }

    /// <summary>
    /// Adds an enum equality using the native enum comparison (no Convert).
    /// </summary>
    public FilterBuilder<T> WhereEnum<TEnum>(string propertyPath, TEnum value) where TEnum : struct, Enum
    {
        _filters.Add(EnumExpressionHelper.Equals<T, TEnum>(propertyPath, value));
        return this;
    }

    // --- Raw ---

    /// <summary>
    /// Adds a raw expression.
    /// </summary>
    public FilterBuilder<T> Where(Expression<Func<T, bool>> filter)
    {
        _filters.Add(filter);
        return this;
    }

    // --- Build ---

    /// <summary>
    /// Combines all filters with AND and returns the result.
    /// If no filters were added, returns <c>x => true</c>.
    /// </summary>
    public Expression<Func<T, bool>> Build()
    {
        if (_filters.Count == 0)
            return x => true;

        var result = _filters[0];
        for (var i = 1; i < _filters.Count; i++)
            result = ExpressionHelper.And(result, _filters[i]);

        return result;
    }

    /// <summary>
    /// Combines all filters with OR and returns the result.
    /// If no filters were added, returns <c>x => false</c>.
    /// </summary>
    public Expression<Func<T, bool>> BuildOr()
    {
        if (_filters.Count == 0)
            return x => false;

        var result = _filters[0];
        for (var i = 1; i < _filters.Count; i++)
            result = ExpressionHelper.Or(result, _filters[i]);

        return result;
    }

    /// <summary>
    /// Returns the number of filters added.
    /// </summary>
    public int Count => _filters.Count;

    /// <summary>
    /// Returns true if no filters have been added.
    /// </summary>
    public bool IsEmpty => _filters.Count == 0;

    /// <summary>
    /// Removes all filters.
    /// </summary>
    public void Clear() => _filters.Clear();
}
