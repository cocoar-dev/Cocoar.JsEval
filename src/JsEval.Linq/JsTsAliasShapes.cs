namespace Cocoar.JsEval.Linq;

/// <summary>
/// Metadata-only shape that describes the PascalCase C# LINQ method aliases
/// which <see cref="JsExpressionTranslator"/> accepts on a JS <c>string</c>
/// in predicate context (via its method-map and reflection fallback).
/// <para>
/// <b>Not instantiated, never registered with Jint.</b> Its sole purpose is
/// to be the reflection source for <see cref="LinqTsContributor"/>, which
/// emits a TypeScript <c>interface String</c> augmentation so Monaco picks
/// up the aliases. The actual runtime behavior goes through the translator's
/// method-map (JS camelCase → C# PascalCase) which maps to <c>string.Contains</c>,
/// <c>string.StartsWith</c>, etc.
/// </para>
/// </summary>
internal interface IJsStringAliases
{
    bool Contains(string value);
    bool StartsWith(string value);
    bool EndsWith(string value);
    int IndexOf(string value);
    string ToLower();
    string ToUpper();
    string Trim();
}

/// <summary>
/// Metadata-only shape for the PascalCase LINQ method aliases the translator
/// accepts on JS arrays in predicate context. See <see cref="IJsStringAliases"/>
/// for the same reflection-target pattern.
/// </summary>
internal interface IJsArrayAliases<T>
{
    bool Any(Func<T, bool>? predicate = null);
    bool All(Func<T, bool> predicate);
    T[] Where(Func<T, bool> predicate);
    R[] Select<R>(Func<T, R> selector);
    T? FirstOrDefault(Func<T, bool>? predicate = null);
    bool Contains(T value);
}
