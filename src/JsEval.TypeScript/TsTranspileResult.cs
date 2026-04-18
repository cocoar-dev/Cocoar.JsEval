using System.Collections.Generic;

namespace Cocoar.JsEval.TypeScript;

/// <summary>
/// Result of <see cref="TsTranspiler.TranspileWithSourceMap"/> — transpiled
/// JavaScript plus the raw source-map JSON and any non-error diagnostics
/// (warnings, suggestions, messages).
///
/// <para>The source map follows the standard <a href="https://tc39.es/source-map/">
/// Source Map v3</a> format. Feed it to Monaco or a sourcemap library to turn
/// a JS runtime-error position (line/column in <see cref="Js"/>) back into the
/// original TypeScript position.</para>
///
/// <para><see cref="Js"/> has the trailing <c>//# sourceMappingURL=…</c> comment
/// stripped — embed the map however you prefer (inline base64, sidecar file, …)
/// without having to edit the JS first.</para>
/// </summary>
public sealed record TsTranspileResult(
    string Js,
    string SourceMap,
    IReadOnlyList<TsDiagnostic> Warnings);
