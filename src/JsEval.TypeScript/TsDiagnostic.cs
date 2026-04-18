namespace Cocoar.JsEval.TypeScript;

/// <summary>
/// TypeScript diagnostic category. Mirrors the TypeScript compiler's
/// <c>DiagnosticCategory</c> enum values so <see cref="TsDiagnostic.Code"/>
/// numbers line up with TypeScript's public error catalogue.
/// </summary>
public enum TsDiagnosticCategory
{
    Warning    = 0,
    Error      = 1,
    Suggestion = 2,
    Message    = 3,
}

/// <summary>
/// One TypeScript compiler diagnostic — a single issue reported by
/// <c>ts.transpileModule</c> (syntax error, suggestion, message, …).
///
/// <see cref="Line"/> and <see cref="Column"/> are 1-based and refer to the
/// source text as seen by the TypeScript compiler — i.e. after the transpiler's
/// <c>new Foo(…)</c> → <c>NewObject(…)</c> rewrite pass. The rewrite preserves
/// line numbers in practice; columns may shift for lines that contained a
/// rewritten constructor.
/// </summary>
public sealed record TsDiagnostic(
    TsDiagnosticCategory Category,
    int Code,
    string Message,
    int Line,
    int Column);
