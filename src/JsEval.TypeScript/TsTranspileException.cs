using System;
using System.Collections.Generic;
using System.Linq;

namespace Cocoar.JsEval.TypeScript;

/// <summary>
/// Thrown by <see cref="TsTranspiler.Transpile"/> and
/// <see cref="TsTranspiler.TranspileWithSourceMap"/> when the TypeScript
/// compiler reports at least one <see cref="TsDiagnosticCategory.Error"/>.
///
/// <para>Note: <c>ts.transpileModule</c> only surfaces *syntax* errors — it
/// doesn't build a full program, so type mismatches like
/// <c>const x: number = "oops"</c> slip through silently. Semantic
/// type-checking requires <c>ts.createProgram</c>, which is out of scope here
/// (see the roadmap for a typecheck-enabled add-on). Use Monaco's language
/// service at authoring time if you want full type checks in the editor.</para>
/// </summary>
public sealed class TsTranspileException : Exception
{
    /// <summary>All diagnostics — errors, warnings, suggestions, messages.</summary>
    public IReadOnlyList<TsDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Just the errors (<see cref="TsDiagnosticCategory.Error"/>). Convenience
    /// accessor for UIs that only want to show blocking issues.
    /// </summary>
    public IReadOnlyList<TsDiagnostic> Errors { get; }

    public TsTranspileException(IReadOnlyList<TsDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
        Errors = diagnostics.Where(d => d.Category == TsDiagnosticCategory.Error).ToList();
    }

    private static string BuildMessage(IReadOnlyList<TsDiagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Category == TsDiagnosticCategory.Error).ToList();
        if (errors.Count == 0)
            return "TypeScript transpile reported no errors (this exception should not have been thrown).";
        var first = errors[0];
        var tail = errors.Count > 1 ? $" (and {errors.Count - 1} more)" : "";
        return $"TS{first.Code} at line {first.Line}, col {first.Column}: {first.Message}{tail}";
    }
}
