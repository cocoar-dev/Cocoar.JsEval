using System;
using System.Collections.Generic;
using System.Linq;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Concrete backing for the <c>Type</c> JS global. Registered automatically in
/// <see cref="JsEngine"/> when <see cref="JsEngineOptions.DiscriminatorMappings"/> is non-empty
/// or <see cref="JsEngineOptions.TypeAliases"/> contains any entry.
/// Scripts call <c>Type.Is(a, 'dog')</c> or <c>Type.Is(a, 'Dog')</c>.
/// <para>
/// Lookup order:
/// <list type="number">
///   <item>Explicit <see cref="DiscriminatorMapping"/> — matched by value and base type.
///   Supports custom <see cref="DiscriminatorMapping.RuntimeCheck"/>.</item>
///   <item>Type-alias fallback — resolves the string via <see cref="JsEngineOptions.TypeAliases"/>.</item>
///   <item>Namespace-mapping fallback — resolves the string as the namespace-mapped short name
///   (e.g. <c>'Dog'</c> for a flatten-to-root mapping, or <c>'MyModels.Dog'</c> for a
///   non-empty target prefix) derived from the concrete types in the discriminator mappings.</item>
/// </list>
/// </para>
/// </summary>
public sealed class JsTypeGlobal
{
    private readonly IReadOnlyList<DiscriminatorMapping> _mappings;
    private readonly IReadOnlyDictionary<string, Type> _typeAliases;
    private readonly Dictionary<string, Type> _namespaceMapped;

    public JsTypeGlobal(
        IReadOnlyList<DiscriminatorMapping> mappings,
        IReadOnlyDictionary<string, Type> typeAliases,
        IReadOnlyList<(string Source, string Target)> namespaceMappings)
    {
        _mappings = mappings;
        _typeAliases = typeAliases;
        _namespaceMapped = BuildNamespaceMapped(mappings, typeAliases, namespaceMappings);
    }

    private static Dictionary<string, Type> BuildNamespaceMapped(
        IReadOnlyList<DiscriminatorMapping> mappings,
        IReadOnlyDictionary<string, Type> typeAliases,
        IReadOnlyList<(string Source, string Target)> namespaceMappings)
    {
        if (namespaceMappings.Count == 0 || mappings.Count == 0)
            return new Dictionary<string, Type>();

        var types = mappings.Select(m => m.ConcreteType).Where(t => t != null).Select(t => t!)
            .Concat(mappings.Select(m => m.BaseType))
            .Distinct();

        var resolutions = TypeAliasResolver.Resolve(types, typeAliases, namespaceMappings);
        var dict = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var r in resolutions)
        {
            if (r.Source != TypeAliasResolutionSource.NamespaceMapping) continue;
            var key = string.IsNullOrEmpty(r.MappedNamespace)
                ? r.ShortName
                : r.MappedNamespace + "." + r.ShortName;
            dict.TryAdd(key, r.Type);
        }
        return dict;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="obj"/> satisfies the check for
    /// <paramref name="discriminatorValue"/>. Tries explicit <see cref="DiscriminatorMapping"/>
    /// entries first; falls back to type aliases, then to namespace-mapped short names.
    /// </summary>
    public bool Is(object obj, string discriminatorValue)
    {
        if (obj == null) return false;

        // 1. Explicit discriminator mapping (CLR-type or property-based, supports RuntimeCheck).
        foreach (var mapping in _mappings)
        {
            if (mapping.Value == discriminatorValue && mapping.BaseType.IsAssignableFrom(obj.GetType()))
                return mapping.IsMatch(obj, discriminatorValue);
        }

        // 2. Type-alias fallback: "Dog" → typeof(Dog) via AddTypeAlias<Dog>().
        if (_typeAliases.TryGetValue(discriminatorValue, out var aliasedType))
            return aliasedType.IsInstanceOfType(obj);

        // 3. Namespace-mapping fallback: "Dog" or "MyModels.Dog" via MapNamespace(...).
        if (_namespaceMapped.TryGetValue(discriminatorValue, out var namespacedType))
            return namespacedType.IsInstanceOfType(obj);

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="obj"/> satisfies the check for any of the
    /// <paramref name="discriminatorValues"/>. Equivalent to
    /// <c>Is(obj, v1) || Is(obj, v2) || …</c>.
    /// </summary>
    public bool IsOneOf(object obj, string[] discriminatorValues)
    {
        if (obj == null) return false;
        foreach (var v in discriminatorValues)
            if (Is(obj, v)) return true;
        return false;
    }
}
