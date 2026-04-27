using System.Globalization;
using System.Linq.Expressions;

namespace Cocoar.JsEval.Linq.Building;

/// <summary>
/// Helpers for building enum comparison expressions that work with different ORM storage strategies.
///
/// The problem: C#'s <c>Expression.Equal</c> for enums generates
/// <c>Convert(x.Status, Int32) == Convert(value, Int32)</c>. This works when the DB stores
/// enums as integers, but fails when they're stored as strings (e.g. Marten with
/// <c>EnumStorage.AsString</c>, EF Core with <c>HasConversion&lt;string&gt;()</c>).
///
/// These helpers let you choose the comparison strategy explicitly.
/// </summary>
internal static class EnumExpressionHelper
{
    /// <summary>
    /// Builds an equality expression using the native enum type — no <c>Convert(Int32)</c>.
    /// Lets the LINQ provider decide how to translate it based on its own configuration.
    /// This is the safest default when you're not sure how the ORM stores enums.
    /// </summary>
    public static Expression<Func<T, bool>> Equals<T, TEnum>(string propertyName, TEnum value) where TEnum : struct, Enum
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(param, propertyName);
        var constant = Expression.Constant(value, typeof(TEnum));
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(property, constant), param);
    }

    /// <summary>
    /// Builds an equality expression from a string value, parsing it to the enum type first.
    /// Uses the native enum type — no <c>Convert(Int32)</c>.
    /// </summary>
    public static Expression<Func<T, bool>> Equals<T>(string propertyName, Type enumType, string value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(param, propertyName);
        var parsed = Enum.Parse(enumType, value, ignoreCase: true);
        var constant = Expression.Constant(parsed, enumType);
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(property, constant), param);
    }

    // --- Explicit storage strategy variants ---

    /// <summary>
    /// Compares enum as string: <c>x.Status.ToString() == "InProgress"</c>.
    /// Use when the DB stores enums as their string name.
    /// </summary>
    public static Expression<Func<T, bool>> EqualsAsString<T>(string propertyName, string value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(param, propertyName);
        var body = BuildAsStringComparison(property, value);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>Compares enum as string from a typed enum value: <c>x.Status.ToString() == value.ToString()</c>.</summary>
    public static Expression<Func<T, bool>> EqualsAsString<T, TEnum>(string propertyName, TEnum value) where TEnum : struct, Enum =>
        EqualsAsString<T>(propertyName, value.ToString());

    /// <summary>
    /// Compares enum as integer: <c>(int)x.Status == (int)value</c>.
    /// Use when the DB stores enums as their numeric value.
    /// </summary>
    public static Expression<Func<T, bool>> EqualsAsInt<T, TEnum>(string propertyName, TEnum value) where TEnum : struct, Enum
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(param, propertyName);
        var body = BuildAsIntComparison(property, value);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>Compares enum as integer from a string value: parses the string, then compares as int.</summary>
    public static Expression<Func<T, bool>> EqualsAsInt<T>(string propertyName, Type enumType, string value)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(param, propertyName);
        var parsed = Enum.Parse(enumType, value, ignoreCase: true);
        var intValue = Convert.ToInt32(parsed, CultureInfo.InvariantCulture);
        var convertProperty = Expression.Convert(property, typeof(int));
        var constant = Expression.Constant(intValue, typeof(int));
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(convertProperty, constant), param);
    }

    // --- Building blocks for custom expression trees ---

    /// <summary>Builds a string comparison: <c>member.ToString() == value</c>.</summary>
    public static Expression BuildAsStringComparison(MemberExpression enumProperty, string value)
    {
        var toStringMethod = typeof(object).GetMethod(nameof(ToString))!;
        var toStringCall = Expression.Call(enumProperty, toStringMethod);
        var constant = Expression.Constant(value, typeof(string));
        return Expression.Equal(toStringCall, constant);
    }

    /// <summary>Builds an integer comparison: <c>(int)member == (int)value</c>.</summary>
    public static Expression BuildAsIntComparison<TEnum>(MemberExpression enumProperty, TEnum value) where TEnum : struct, Enum
    {
        var convertProperty = Expression.Convert(enumProperty, typeof(int));
        var intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        var constant = Expression.Constant(intValue, typeof(int));
        return Expression.Equal(convertProperty, constant);
    }
}
