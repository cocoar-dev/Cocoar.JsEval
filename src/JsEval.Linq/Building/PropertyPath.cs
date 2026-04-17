using System.Linq.Expressions;

namespace Cocoar.JsEval.Linq.Building;

/// <summary>
/// Resolves dotted property paths (e.g., "Customer.Name") to <see cref="MemberExpression"/>s.
/// Handy when you already have a path as a string (config, UI-driven filters, REST request)
/// and want to plug it into an Expression tree.
/// </summary>
internal static class PropertyPath
{
    /// <summary>
    /// Resolves a dotted property path to a <see cref="MemberExpression"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var param = Expression.Parameter(typeof(TodoView), "x");
    /// var expr  = PropertyPath.Resolve(param, "Customer.Name");
    /// // → x.Customer.Name
    /// </code>
    /// </example>
    public static MemberExpression Resolve(Expression root, string path)
    {
        Expression current = root;
        foreach (var part in path.Split('.'))
            current = Expression.Property(current, part);
        return (MemberExpression)current;
    }

    /// <summary>
    /// Returns the final property type of a dotted path.
    /// </summary>
    /// <example>
    /// <code>
    /// var type = PropertyPath.GetPropertyType(typeof(TodoView), "Customer.Name");
    /// // → typeof(string)
    /// </code>
    /// </example>
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
    /// Builds a non-generic lambda <c>x =&gt; x.Customer.Name</c> for OrderBy / Select etc.
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
