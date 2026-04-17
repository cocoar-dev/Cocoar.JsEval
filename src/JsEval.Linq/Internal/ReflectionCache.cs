using System.Collections.Concurrent;
using System.Reflection;

namespace Cocoar.JsEval.Linq.Internal;

/// <summary>
/// Thread-safe caches for the reflection lookups the translator performs on
/// every call. Reflection info is immutable, so the caches never need eviction.
/// </summary>
internal static class ReflectionCache
{
    // --- Property lookup (case-insensitive name) ---

    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> _properties =
        new(TypeAndNameCiComparer.Instance);

    public static PropertyInfo? GetProperty(Type type, string name) =>
        _properties.GetOrAdd((type, name), static k =>
            k.Type.GetProperty(k.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase));

    // --- Instance method lookup (name + arg-type signature, case-insensitive name) ---

    private static readonly ConcurrentDictionary<MethodKey, MethodInfo?> _methods = new();

    public static MethodInfo? GetMethod(Type type, string name, Type[] argTypes) =>
        _methods.GetOrAdd(new MethodKey(type, name, argTypes), static k =>
            k.Type.GetMethod(k.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase,
                binder: null, types: k.ArgTypes, modifiers: null));

    // --- User-defined implicit conversion op ---

    private static readonly ConcurrentDictionary<(Type From, Type To), MethodInfo?> _implicitOps = new();

    public static MethodInfo? GetImplicitCastMethod(Type from, Type to) =>
        _implicitOps.GetOrAdd((from, to), static k =>
            Cocoar.Reflectensions.ExtensionMethods.TypeExtensions.GetImplicitCastMethodTo(k.From, k.To));

    // --- Queryable.* generic method definitions (for OrderBy/ThenBy/...) ---

    private static readonly ConcurrentDictionary<string, MethodInfo> _queryableByName2Args =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the open-generic 2-arg <see cref="Queryable"/> method with the given name
    /// (e.g. <c>Queryable.OrderBy&lt;T,TKey&gt;(IQueryable&lt;T&gt;, Expression&lt;Func&lt;T,TKey&gt;&gt;)</c>).
    /// </summary>
    public static MethodInfo GetQueryable2ArgMethod(string name) =>
        _queryableByName2Args.GetOrAdd(name, static n =>
            typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == n
                    && m.IsGenericMethodDefinition
                    && m.GetGenericArguments().Length == 2
                    && m.GetParameters().Length == 2));

    /// <summary>Closed-generic Queryable method for (name, T, TKey).</summary>
    private static readonly ConcurrentDictionary<(string Name, Type T, Type TKey), MethodInfo> _queryableClosed = new();

    public static MethodInfo GetQueryableClosed(string name, Type t, Type tKey) =>
        _queryableClosed.GetOrAdd((name, t, tKey), static k =>
            GetQueryable2ArgMethod(k.Name).MakeGenericMethod(k.T, k.TKey));

    // --- Enumerable.* for array-method translation (lambda-taking 2-arg variant) ---

    private static readonly ConcurrentDictionary<string, MethodInfo> _enumerableLambdaByName =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the 2-parameter open-generic <see cref="Enumerable"/> method with the given
    /// name, where the second parameter is the generic predicate/selector (excludes the
    /// non-lambda 2-arg overloads like <c>Contains</c>).
    /// </summary>
    public static MethodInfo GetEnumerableLambdaMethod(string name) =>
        _enumerableLambdaByName.GetOrAdd(name, static n =>
            typeof(Enumerable).GetMethods()
                .First(m => m.Name == n
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.IsGenericType));

    private static readonly ConcurrentDictionary<string, MethodInfo> _enumerableContains = new();

    public static MethodInfo GetEnumerableContainsClosed(Type elementType) =>
        _enumerableContains.GetOrAdd(elementType.FullName!, _ =>
            typeof(Enumerable).GetMethods()
                .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
                .MakeGenericMethod(elementType));

    private static readonly ConcurrentDictionary<(string Name, Type Elem, Type? Result), MethodInfo> _enumerableClosed = new();

    public static MethodInfo GetEnumerableLambdaClosed(string name, Type elementType, Type? resultType) =>
        _enumerableClosed.GetOrAdd((name, elementType, resultType), static k =>
        {
            var open = GetEnumerableLambdaMethod(k.Name);
            return k.Result is null
                ? open.MakeGenericMethod(k.Elem)
                : open.MakeGenericMethod(k.Elem, k.Result);
        });

    // --- Equality helpers ---

    private sealed class TypeAndNameCiComparer : IEqualityComparer<(Type Type, string Name)>
    {
        public static readonly TypeAndNameCiComparer Instance = new();

        public bool Equals((Type Type, string Name) x, (Type Type, string Name) y) =>
            x.Type == y.Type && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Type Type, string Name) obj) =>
            HashCode.Combine(obj.Type, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }

    private sealed class MethodKey : IEquatable<MethodKey>
    {
        public readonly Type Type;
        public readonly string Name;
        public readonly Type[] ArgTypes;
        private readonly int _hash;

        public MethodKey(Type type, string name, Type[] argTypes)
        {
            Type = type;
            Name = name;
            ArgTypes = argTypes;
            var h = new HashCode();
            h.Add(type);
            h.Add(name, StringComparer.OrdinalIgnoreCase);
            foreach (var t in argTypes) h.Add(t);
            _hash = h.ToHashCode();
        }

        public bool Equals(MethodKey? other) =>
            other is not null
            && Type == other.Type
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && ArgTypes.Length == other.ArgTypes.Length
            && ArgTypes.AsSpan().SequenceEqual(other.ArgTypes.AsSpan());

        public override bool Equals(object? obj) => obj is MethodKey k && Equals(k);
        public override int GetHashCode() => _hash;
    }
}
