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
    /// Resolves a free identifier in a rule to a host object, and is consulted
    /// before the engine's own closure lookup. This is how a caller exposes
    /// something the engine cannot resolve on its own — most importantly an
    /// imported module, whose binding is module-scoped rather than global and
    /// therefore invisible to the engine lookup.
    ///
    /// The returned object is used at translation time only. A call on it whose
    /// arguments are all constant is evaluated immediately and folded into a
    /// <c>ConstantExpression</c>, so what reaches the LINQ provider is the
    /// result, not a call the provider would have to translate or run per row.
    ///
    /// Return <c>null</c> for names this resolver does not know.
    /// </summary>
    public Func<string, object?>? IdentifierResolver { get; init; }

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

    /// <summary>
    /// Maximum AST recursion depth before the translator aborts with
    /// <see cref="InvalidOperationException"/>. Guards against
    /// <see cref="StackOverflowException"/> on deeply nested scripts (e.g. long
    /// ternary chains or logical expressions), which is unrecoverable in .NET
    /// and would crash the host process. Default: <c>256</c> — comfortably above
    /// any hand-written predicate (typical depth &lt; 10) but well below the
    /// default thread stack's recursion limit for <c>Visit</c>-class frames.
    /// </summary>
    public int MaxAstDepth { get; init; } = 256;
}
