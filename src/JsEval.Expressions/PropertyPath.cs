using System;
using System.Linq.Expressions;

namespace Cocoar.JsEval.Expressions;

/// <summary>
/// Resolves dotted property paths (e.g., "Customer.Name") to MemberExpressions.
/// Supports nested navigation properties of any depth.
/// </summary>
public static class PropertyPath
{
    /// <summary>
    /// Resolves a dotted property path to a <see cref="MemberExpression"/>.
    ///
    /// <example>
    /// <code>
    /// var param = Expression.Parameter(typeof(TodoView), "x");
    /// var expr = PropertyPath.Resolve(param, "Customer.Name");
    /// // → x.Customer.Name
    /// </code>
    /// </example>
    /// </summary>
    public static MemberExpression Resolve(Expression root, string path)
    {
        Expression current = root;
        foreach (var part in path.Split('.'))
        {
            current = Expression.Property(current, part);
        }

        return (MemberExpression)current;
    }

    /// <summary>
    /// Returns the final property type of a dotted path.
    ///
    /// <example>
    /// <code>
    /// var type = PropertyPath.GetPropertyType(typeof(TodoView), "Customer.Name");
    /// // → typeof(string)
    /// </code>
    /// </example>
    /// </summary>
    public static Type GetPropertyType(Type rootType, string path)
    {
        var type = rootType;
        foreach (var part in path.Split('.'))
        {
            var prop = type.GetProperty(part)
                ?? throw new ArgumentException($"Property '{part}' not found on type '{type.Name}'");
            type = prop.PropertyType;
        }
        return type;
    }

    /// <summary>
    /// Builds a lambda expression for a property path: <c>x => x.Customer.Name</c>.
    /// Useful for OrderBy, Select, etc.
    /// </summary>
    public static LambdaExpression BuildSelector(Type entityType, string path)
    {
        var param = Expression.Parameter(entityType, "x");
        var property = Resolve(param, path);
        return Expression.Lambda(property, param);
    }

    /// <summary>
    /// Builds a typed lambda: <c>Expression&lt;Func&lt;T, TResult&gt;&gt;</c>.
    /// </summary>
    public static Expression<Func<T, TResult>> BuildSelector<T, TResult>(string path)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = Resolve(param, path);
        return Expression.Lambda<Func<T, TResult>>(property, param);
    }
}
