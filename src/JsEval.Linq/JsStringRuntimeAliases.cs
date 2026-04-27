namespace Cocoar.JsEval.Linq;

/// <summary>
/// Real C# extension methods on <see cref="string"/> so Jint can resolve the
/// PascalCase C# LINQ aliases at plain runtime, not just inside predicate
/// lambdas translated by <see cref="JsExpressionTranslator"/>.
/// <para>
/// Registered via <see cref="Cocoar.JsEval.Engine.JsEvalBuilder.AddExtensionMethods"/>
/// from <c>AddLinq()</c>. Jint's extension-method resolution walks these for any
/// call on a string value, so a plain script like <c>"abc".Contains("x")</c> or
/// <c>"HELLO".ToLower()</c> hits the same BCL implementation as C# would.
/// </para>
/// <para>
/// These also stay aligned with the metadata shape in
/// <see cref="IJsStringAliases"/>: same signatures → <see cref="LinqTsContributor"/>
/// generates the TypeScript <c>interface String</c> augmentation that matches.
/// </para>
/// </summary>
public static class JsStringRuntimeAliases
{
    public static bool Contains(this string s, string value) => s.Contains(value, StringComparison.Ordinal);
    public static bool StartsWith(this string s, string value) => s.StartsWith(value, StringComparison.Ordinal);
    public static bool EndsWith(this string s, string value) => s.EndsWith(value, StringComparison.Ordinal);
    public static int IndexOf(this string s, string value) => s.IndexOf(value, StringComparison.Ordinal);
    public static string ToLower(this string s) => s.ToLowerInvariant();
    public static string ToUpper(this string s) => s.ToUpperInvariant();
    public static string Trim(this string s) => s.Trim();
}
