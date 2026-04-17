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
}
