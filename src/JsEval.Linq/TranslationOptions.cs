using System;
using System.Collections.Generic;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq.MethodMapping;

namespace Cocoar.JsEval.Linq;

/// <summary>Options controlling <see cref="JsExpressionTranslator"/> behaviour.</summary>
public sealed class TranslationOptions
{
    /// <summary>Method resolution strategy. Default: <see cref="DefaultJsMethodMap"/>.</summary>
    public IJsMethodMap MethodMap { get; init; } = new DefaultJsMethodMap();

    /// <summary>
    /// If <c>true</c> (default), numeric literals (JS double) are coerced to the
    /// target property's CLR type (int/long/decimal/...) in binary comparisons.
    /// </summary>
    public bool CoerceNumericLiterals { get; init; } = true;

    /// <summary>
    /// Discriminator mappings for polymorphic types. When a script calls
    /// <c>Type.Is(param, 'value')</c> and a mapping matches, the call is rewritten to
    /// <c>param is ConcreteType</c>. Inside an AND-conjunction, subsequent member
    /// accesses on the same parameter narrow to the matched concrete type so that
    /// subtype-only properties resolve correctly.
    /// </summary>
    public IReadOnlyList<DiscriminatorMapping> DiscriminatorMappings { get; init; } = [];

    /// <summary>
    /// Short-name type aliases used as a fallback when <c>Type.Is(param, 'Dog')</c>
    /// has no matching <see cref="DiscriminatorMappings"/> entry. The string value
    /// is resolved to a CLR <see cref="Type"/> and the call is rewritten to
    /// <c>param is ResolvedType</c>.
    /// <para>
    /// Populate from <c>JsEngineOptions.TypeAliases</c> to share the same aliases
    /// configured on the engine builder:
    /// </para>
    /// <code>
    /// new TranslationOptions { TypeAliases = engine.Options.TypeAliases }
    /// </code>
    /// </summary>
    public IReadOnlyDictionary<string, Type> TypeAliases { get; init; } =
        new Dictionary<string, Type>();

    /// <summary>
    /// Namespace mappings used as a last-resort fallback when <c>Type.Is(param, 'Dog')</c>
    /// has no matching <see cref="DiscriminatorMappings"/> entry and no <see cref="TypeAliases"/>
    /// entry. The string is matched against the namespace-resolved short name of each concrete
    /// type in <see cref="DiscriminatorMappings"/> (e.g. <c>'Dog'</c> for a flatten-to-root
    /// mapping or <c>'MyModels.Dog'</c> for a non-empty target prefix).
    /// <para>
    /// Populate from <c>JsEngineOptions.NamespaceMappings</c>:
    /// </para>
    /// <code>
    /// new TranslationOptions { NamespaceMappings = engine.Options.NamespaceMappings }
    /// </code>
    /// </summary>
    public IReadOnlyList<(string Source, string Target)> NamespaceMappings { get; init; } = [];
}
