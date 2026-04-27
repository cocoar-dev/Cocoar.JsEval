using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
using Cocoar.JsEval;
using Jint;
using Jint.Native;

namespace Cocoar.JsEval.TypeScript;

/// <summary>
/// Transpiles TypeScript source code to JavaScript (ESNext target).
/// Use this to compile TypeScript once (e.g., at save time), then execute
/// the resulting JavaScript with a <c>JsEngine</c> instance.
///
/// <para><b>Error handling (since 3.1.2).</b> Throws <see cref="TsTranspileException"/>
/// on any syntax error reported by the TypeScript compiler. Non-error diagnostics
/// (warnings, suggestions, messages) are returned via <see cref="TranspileWithSourceMap"/>;
/// in the simpler <see cref="Transpile(string)"/> path they're dropped silently.</para>
///
/// <para><b>Limitation.</b> Internally uses <c>ts.transpileModule</c>, which only
/// performs syntactic checks. Type mismatches like <c>const x: number = "oops"</c>
/// are not reported — they require a full <c>ts.createProgram</c> pass (out of scope
/// for this package).</para>
/// </summary>
public sealed class TsTranspiler
{
    private static readonly ScriptParsingOptions ParsingOptions = new() { Tolerant = true };
    private static Prepared<Script>? _typeScriptScript;
    private static readonly ConcurrentBag<Engine> _enginePool = new();
    private static readonly Regex ClrConstructorRegex = new(@"new\s(?<typeName>[a-zA-Z0-9_\.\s<>\[\]$,]+)\((?<parameters>[a-zA-Z0-9_\.,\s<>\[\]$'""]+)?\)(;)?(?<ignore>//ignore)?", RegexOptions.Compiled);
    private static readonly Regex SourceMappingUrlComment = new(@"^\s*//# sourceMappingURL=.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Transpile TypeScript source code to JavaScript.
    ///
    /// Throws <see cref="TsTranspileException"/> if the TypeScript compiler
    /// reports any error. Non-error diagnostics are dropped; use
    /// <see cref="TranspileWithSourceMap"/> if you need them.
    /// </summary>
    // CA1822: kept as instance methods — public API; callers hold TsTranspiler instances
    // (e.g., pooling wrappers and existing test code). Making them static is a source-breaking
    // change because C# does not allow calling static members through instance references.
#pragma warning disable CA1822
    public string Transpile(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return "";
        var (js, _, _) = TranspileCore(sourceCode, emitSourceMap: false);
        return js;
    }

    /// <summary>
    /// Transpile TypeScript source code and return the JavaScript output,
    /// the source map (Source Map v3 JSON), and any non-error diagnostics
    /// (warnings / suggestions / messages).
    ///
    /// Throws <see cref="TsTranspileException"/> on any error-category
    /// diagnostic — the same error contract as <see cref="Transpile(string)"/>.
    /// </summary>
    public TsTranspileResult TranspileWithSourceMap(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return new TsTranspileResult("", "", []);
        var (js, map, warnings) = TranspileCore(sourceCode, emitSourceMap: true);
        return new TsTranspileResult(js, map ?? "", warnings);
    }
#pragma warning restore CA1822

    private static (string Js, string? SourceMap, IReadOnlyList<TsDiagnostic> Warnings) TranspileCore(
        string sourceCode, bool emitSourceMap)
    {
        var rewritten = RewriteClrConstructors(sourceCode);
        EnsureTypeScriptCompilerLoaded();

        var engine = RentEngine();
        try
        {
            engine.SetValue("src", rewritten);

            // `ts.transpileModule` returns `{ outputText, diagnostics, sourceMapText }`.
            // We project the diagnostics into a flat shape inline so we don't have to
            // touch Jint's Diagnostic prototype (which holds object refs to SourceFile
            // etc. that we don't need).
            var projection = $$$"""
                (function () {
                  var result = ts.transpileModule(src, {
                    "reportDiagnostics": true,
                    "compilerOptions": {
                      "target": "ESNext",
                      "module": "ESNext",
                      "sourceMap": {{{(emitSourceMap ? "true" : "false")}}}
                    }
                  });
                  return {
                    outputText: result.outputText,
                    sourceMapText: result.sourceMapText || null,
                    diagnostics: (result.diagnostics || []).map(function (d) {
                      return {
                        category: d.category,
                        code: d.code,
                        message: ts.flattenDiagnosticMessageText(d.messageText, '\n'),
                        start: (d.start == null ? -1 : d.start)
                      };
                    })
                  };
                })()
                """;

            var resultObj = engine.Evaluate(projection, ParsingOptions).AsObject();

            var js = resultObj.Get("outputText").AsString();
            var sourceMapVal = resultObj.Get("sourceMapText");
            string? sourceMap = sourceMapVal.IsNull() || sourceMapVal.IsUndefined()
                ? null : sourceMapVal.AsString();

            var diagnostics = ExtractDiagnostics(resultObj.Get("diagnostics"), rewritten);
            var errors = diagnostics.Where(d => d.Category == TsDiagnosticCategory.Error).ToList();
            if (errors.Count > 0)
                throw new TsTranspileException(diagnostics);

            if (emitSourceMap)
                js = SourceMappingUrlComment.Replace(js, "").TrimEnd('\r', '\n');

            var warnings = diagnostics
                .Where(d => d.Category != TsDiagnosticCategory.Error)
                .ToList();
            return (js, sourceMap, warnings);
        }
        finally
        {
            ReturnEngine(engine);
        }
    }

    private static List<TsDiagnostic> ExtractDiagnostics(JsValue diagnosticsValue, string sourceText)
    {
        if (!diagnosticsValue.IsArray())
            return [];
        var arr = diagnosticsValue.AsArray();
        var length = (int)arr.Length;
        if (length == 0) return [];

        var list = new List<TsDiagnostic>(length);
        for (uint i = 0; i < length; i++)
        {
            var d = arr.Get(i).AsObject();
            var category = (int)d.Get("category").AsNumber();
            var code = (int)d.Get("code").AsNumber();
            var message = d.Get("message").AsString();
            var start = (int)d.Get("start").AsNumber();
            var (line, col) = GetLineColumn(sourceText, start);
            list.Add(new TsDiagnostic(
                (TsDiagnosticCategory)category, code, message, line, col));
        }
        return list;
    }

    /// <summary>
    /// Converts a zero-based character offset in <paramref name="source"/> to a
    /// 1-based (line, column) pair. Returns <c>(1, 1)</c> for negative or
    /// out-of-range offsets — matches how editors typically render
    /// "unlocatable" diagnostics.
    /// </summary>
    private static (int Line, int Column) GetLineColumn(string source, int offset)
    {
        if (offset < 0 || offset > source.Length) return (1, 1);
        int line = 1, col = 1;
        for (var i = 0; i < offset; i++)
        {
            if (source[i] == '\n') { line++; col = 1; }
            else col++;
        }
        return (line, col);
    }

    private static Engine RentEngine()
    {
        if (_enginePool.TryTake(out var engine))
            return engine;

        var newEngine = new Engine();
        newEngine.Execute(_typeScriptScript!.Value);
        return newEngine;
    }

    private static void ReturnEngine(Engine engine) => _enginePool.Add(engine);

    private static string RewriteClrConstructors(string sourceCode)
    {
        var matches = ClrConstructorRegex.Matches(sourceCode);

        // Process matches in reverse order to avoid index shifting during replacements
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];

            if (match.Groups["ignore"].Success)
                continue;

            var typeName = match.Groups["typeName"].Value;
            var type = TypeHelper.FindConstructorReplaceType(typeName);

            if (type is not null)
            {
                var parameters = match.Groups["parameters"].Value;
                var constructorParams = !string.IsNullOrWhiteSpace(parameters)
                    ? $", [{parameters}]"
                    : "";

                var replacement = $"NewObject('{typeName}'{constructorParams})";
                sourceCode = string.Concat(
                    sourceCode[..match.Index],
                    replacement,
                    sourceCode[(match.Index + match.Length)..]);
            }
        }

        return sourceCode;
    }

    private static void EnsureTypeScriptCompilerLoaded()
    {
        if (_typeScriptScript is not null)
            return;

        var tsLib = GetFromResources("typescript.min.js");
        _typeScriptScript = Jint.Engine.PrepareScript(tsLib);
    }

    private static string GetFromResources(string resourceName)
    {
        var type = typeof(TsTranspiler);

        using var stream = type.Assembly.GetManifestResourceStream($"{type.Namespace}.{resourceName}");
        if (stream is null)
            throw new FileNotFoundException($"Embedded resource '{type.Namespace}.{resourceName}' not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
