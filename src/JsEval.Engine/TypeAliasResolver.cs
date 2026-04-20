using System;
using System.Collections.Generic;
using System.Linq;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Resolves the effective short-name rendering for a set of types given
/// the user-configured explicit <see cref="JsEngineOptions.TypeAliases"/>
/// and <see cref="JsEngineOptions.NamespaceMappings"/>.
///
/// <para>Consumed by both <c>JsEngine</c> (for <c>NewObject(...)</c> name →
/// type lookup at runtime) and <c>TsDefinitionService</c> (for <c>.d.ts</c>
/// rendering at save time) so both stay in sync with a single source of
/// truth.</para>
///
/// <para>Collision policy: two distinct types resolving to the same
/// <c>(mapped-namespace, short-name)</c> pair throw
/// <see cref="InvalidOperationException"/> with both source types listed
/// and actionable next steps. No types are silently dropped or overridden.
/// Types whose namespace starts with <c>System.</c> are exempt from
/// namespace mapping by default (they stay fully qualified).</para>
/// </summary>
public static class TypeAliasResolver
{
    /// <summary>
    /// Result of resolving one type.
    /// <list type="bullet">
    ///   <item><description><see cref="ShortName"/> — the name emitted in the <c>.d.ts</c> and registered as a <c>NewObject</c> key</description></item>
    ///   <item><description><see cref="MappedNamespace"/> — the (possibly empty) namespace wrapper the type should live under; empty means root scope</description></item>
    ///   <item><description><see cref="Source"/> — how this name was chosen (explicit alias, namespace mapping, or unchanged)</description></item>
    /// </list>
    /// </summary>
    public sealed record Resolution(
        Type Type,
        string ShortName,
        string MappedNamespace,
        TypeAliasResolutionSource Source);

    /// <summary>
    /// Resolve every given type into its effective rendering, detecting collisions.
    /// </summary>
    /// <param name="types">The types to resolve. Distinct type identities only.</param>
    /// <param name="explicitAliases">Explicit alias → type map from <see cref="JsEngineOptions.TypeAliases"/>.</param>
    /// <param name="namespaceMappings">Ordered namespace prefix mappings from <see cref="JsEngineOptions.NamespaceMappings"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when two distinct types end up with the same
    /// <c>(MappedNamespace, ShortName)</c> pair. Message lists all colliding
    /// types and suggests three fixes.
    /// </exception>
    public static IReadOnlyList<Resolution> Resolve(
        IEnumerable<Type> types,
        IReadOnlyDictionary<string, Type> explicitAliases,
        IReadOnlyList<(string Source, string Target)> namespaceMappings)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(explicitAliases);
        ArgumentNullException.ThrowIfNull(namespaceMappings);

        // Reverse the explicit-alias map once so we can look up "does this type
        // have an explicit alias?" without scanning the dict.
        var aliasByType = new Dictionary<Type, string>();
        foreach (var kv in explicitAliases)
            aliasByType[kv.Value] = kv.Key;

        var results = new List<Resolution>();
        // Track what's been claimed so we can detect collisions.
        // Key: (mappedNamespace, shortName)  — Value: list of types that want this slot.
        var claimed = new Dictionary<(string Ns, string Name), List<Type>>();

        foreach (var rawType in types.Distinct())
        {
            // Phantom compound types that have no runtime identity of their own:
            //   `T` / `TSelf`              — generic type parameters (IsGenericParameter)
            //   `T&` / `T[]` / `T*`        — ByRef / array / pointer wrapping a parameter (FullName is null)
            // Skip them; they're artifacts of reflection's type-walker, not rendered types.
            if (rawType.IsGenericParameter || rawType.FullName is null)
                continue;

            var type = rawType.IsGenericType && !rawType.IsGenericTypeDefinition
                ? rawType.GetGenericTypeDefinition()
                : rawType;

            string mappedNs;
            string shortName;
            TypeAliasResolutionSource source;

            // (1) Explicit alias wins over everything.
            if (aliasByType.TryGetValue(type, out var explicitAlias))
            {
                mappedNs = "";
                shortName = explicitAlias;
                source = TypeAliasResolutionSource.ExplicitAlias;
            }
            else
            {
                var ns = type.Namespace ?? "";
                // Mirror TypeDefinition.FromType's naming: strip the backtick
                // from the CLR name, then append `$N` (arity) for generic type
                // definitions so `IComparable` and `IComparable<T>` stay
                // distinct (IComparable vs IComparable$1) — otherwise unrelated
                // generic arities would collide on an identical short name.
                var baseName = StripBacktick(type.Name);
                if (type.IsGenericTypeDefinition)
                    baseName += "$" + type.GetGenericArguments().Length;

                // (2) Namespace mapping — but System.* is excluded by default so it
                //     stays fully qualified. If a consumer really wants to map System,
                //     they can add an explicit AddTypeAlias for a specific type.
                if (!string.IsNullOrEmpty(ns) && !ns.StartsWith("System", StringComparison.Ordinal)
                    && TryApplyNamespaceMapping(ns, namespaceMappings, out var target))
                {
                    mappedNs = target;
                    shortName = baseName;
                    source = TypeAliasResolutionSource.NamespaceMapping;
                }
                else
                {
                    // (3) No rule matched — keep as-is.
                    mappedNs = ns;
                    shortName = baseName;
                    source = TypeAliasResolutionSource.Unchanged;
                }
            }

            var key = (mappedNs, shortName);
            if (!claimed.TryGetValue(key, out var list))
            {
                list = new List<Type>();
                claimed[key] = list;
            }
            list.Add(type);

            results.Add(new Resolution(type, shortName, mappedNs, source));
        }

        // Collision detection — fail loudly when a user-configured rule (explicit
        // alias or namespace mapping) lands two distinct types on the same key.
        //
        // When all colliding types are Unchanged (neither alias nor mapping
        // touched them), this is a natural same-name collision between e.g.
        // `Span<T>+Enumerator` and `ReadOnlySpan<T>+Enumerator` inside `System`.
        // TypeScript accepts those via interface merging (the pre-v3.1.4 behavior
        // silently produced two matching `interface Enumerator$1<T>` blocks);
        // keep that working so the fix doesn't regress consumers that never
        // asked for short-names. Once any rule is involved, we throw — the user
        // is now explicitly shaping the output and deserves immediate feedback.
        var resolutionByType = results.ToDictionary(r => r.Type, r => r.Source);
        var collisions = claimed
            .Where(kv => kv.Value.Count > 1)
            .Where(kv => kv.Value.Any(t => resolutionByType[t] != TypeAliasResolutionSource.Unchanged))
            .ToList();
        if (collisions.Count > 0)
        {
            var lines = new List<string> { "Type alias/mapping collision — two or more distinct types resolve to the same short name:" };
            foreach (var (key, typeList) in collisions)
            {
                var where = string.IsNullOrEmpty(key.Ns) ? "at root scope" : $"in namespace '{key.Ns}'";
                lines.Add($"  '{key.Name}' {where}:");
                foreach (var t in typeList)
                    lines.Add($"    - {t.FullName}");
            }
            lines.Add("");
            lines.Add("Resolve by one of:");
            lines.Add("  - Adding an explicit AddTypeAlias(typeof(X), \"UniqueName\") on one of them");
            lines.Add("  - Narrowing MapNamespace(...) to only one source prefix");
            lines.Add("  - Using a non-empty target prefix in MapNamespace to disambiguate");
            lines.Add("  - Excluding one of the types from the builder");
            throw new InvalidOperationException(string.Join(Environment.NewLine, lines));
        }

        return results;
    }

    private static bool TryApplyNamespaceMapping(
        string ns,
        IReadOnlyList<(string Source, string Target)> mappings,
        out string target)
    {
        foreach (var (source, targetPrefix) in mappings)
        {
            // Semantics:
            //   target = ""        → full flatten: everything under `source` lands at root,
            //                        regardless of how deeply nested the type sat.
            //   target = "Legacy"  → strip + prepend: `X.Y.Z` under source "X" becomes `Legacy.Y.Z`,
            //                        preserving sub-namespace structure.
            // The flatten case is separated out because dropping the prefix while keeping
            // sub-namespaces (e.g. leaving `Customers.CustomerView` intact under Root) is
            // almost never what the caller wants — the typical use is "I want all my
            // projection types at the top, Monaco's 80-char hover path gone". If someone
            // really wants prefix-stripping with sub-namespaces preserved, they can use
            // a non-empty target of their choice (e.g. "Flat") or chain multiple
            // MapNamespace calls for each leaf namespace.
            if (source.Length == 0)
            {
                // Empty source prefix matches every namespace.
                target = string.IsNullOrEmpty(targetPrefix) ? "" : CombineNamespace(targetPrefix, ns);
                return true;
            }
            if (ns.Equals(source, StringComparison.Ordinal))
            {
                target = targetPrefix;
                return true;
            }
            if (ns.StartsWith(source + ".", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(targetPrefix))
                {
                    target = ""; // fully flatten
                }
                else
                {
                    var tail = ns[(source.Length + 1)..];
                    target = $"{targetPrefix}.{tail}";
                }
                return true;
            }
        }
        target = ns;
        return false;
    }

    private static string CombineNamespace(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        return $"{a}.{b}";
    }

    private static string StripBacktick(string name)
    {
        var idx = name.IndexOf('`');
        return idx >= 0 ? name[..idx] : name;
    }
}

public enum TypeAliasResolutionSource
{
    /// <summary>The type has no matching alias or namespace mapping; emitted as-is.</summary>
    Unchanged,
    /// <summary>The user registered an explicit <c>AddTypeAlias</c> for this type.</summary>
    ExplicitAlias,
    /// <summary>The type matched a <c>MapNamespace(...)</c> rule.</summary>
    NamespaceMapping,
}
