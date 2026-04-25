using System;
using System.Reflection;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Maps a discriminator string value to a type check. Three strategies:
/// <list type="bullet">
///   <item><b>CLR-type</b> (<see cref="ConcreteType"/> set, <see cref="PropertyName"/> null) —
///   <c>Type.Is(p, 'dog')</c> → <c>p is Dog</c>. EF Core TPH, LINQ2DB hierarchies.
///   Monaco narrows to <c>Dog</c>.</item>
///   <item><b>Property-based</b> (<see cref="PropertyName"/> set, <see cref="ConcreteType"/> null) —
///   <c>Type.Is(p, 'person')</c> → <c>p.ParticipantType == "person"</c>. Any LINQ provider
///   including Marten. No Monaco narrowing (no CLR subtype).</item>
///   <item><b>Combined</b> (both set) — LINQ uses property equality (Marten-safe);
///   Monaco narrows to <see cref="ConcreteType"/> for IntelliSense.</item>
/// </list>
/// </summary>
public sealed class DiscriminatorMapping
{
    public Type BaseType { get; }
    public string Value { get; }

    /// <summary>CLR subtype for Monaco narrowing and CLR-type LINQ strategy. Null for property-only.</summary>
    public Type? ConcreteType { get; }

    /// <summary>Property name for property equality LINQ strategy. Null for CLR-type-only.</summary>
    public string? PropertyName { get; }

    /// <summary>Optional custom runtime check. When set, overrides the default <see cref="IsMatch"/> logic.</summary>
    public Func<object, bool>? RuntimeCheck { get; }

    internal Func<object, string, bool> IsMatch { get; }

    /// <summary>CLR-type: <c>Type.Is(p, value)</c> → <c>p is ConcreteType</c>.</summary>
    public DiscriminatorMapping(Type baseType, string value, Type concreteType, Func<object, bool>? runtimeCheck = null)
    {
        BaseType = baseType;
        Value = value;
        ConcreteType = concreteType;
        RuntimeCheck = runtimeCheck;
        IsMatch = runtimeCheck != null
            ? (obj, _) => runtimeCheck(obj)
            : (obj, _) => concreteType.IsInstanceOfType(obj);
    }

    /// <summary>Property-based: <c>Type.Is(p, value)</c> → <c>p.PropertyName == value</c>.</summary>
    public DiscriminatorMapping(Type baseType, string value, string propertyName, Func<object, bool>? runtimeCheck = null)
    {
        BaseType = baseType;
        Value = value;
        PropertyName = propertyName;
        RuntimeCheck = runtimeCheck;
        IsMatch = BuildPropertyMatch(baseType, propertyName, runtimeCheck);
    }

    /// <summary>
    /// Combined: LINQ uses <c>p.PropertyName == value</c> (works with Marten);
    /// Monaco narrows to <paramref name="concreteType"/> for IntelliSense.
    /// </summary>
    public DiscriminatorMapping(Type baseType, string value, Type concreteType, string propertyName, Func<object, bool>? runtimeCheck = null)
    {
        BaseType = baseType;
        Value = value;
        ConcreteType = concreteType;
        PropertyName = propertyName;
        RuntimeCheck = runtimeCheck;
        IsMatch = BuildPropertyMatch(baseType, propertyName, runtimeCheck);
    }

    private static Func<object, string, bool> BuildPropertyMatch(Type baseType, string propertyName, Func<object, bool>? runtimeCheck)
    {
        if (runtimeCheck != null)
            return (obj, _) => runtimeCheck(obj);
        var prop = baseType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new ArgumentException($"Property '{propertyName}' not found on '{baseType.Name}'.", nameof(propertyName));
        return (obj, v) => prop.GetValue(obj)?.ToString() == v;
    }
}

