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
    /// Maximum nesting depth (parens, brackets, braces) the source may contain
    /// before <see cref="Transpile"/> rejects it with a <see cref="TsTranspileException"/>.
    /// Default: <c>128</c>.
    /// <para>
    /// The TypeScript compiler runs as JavaScript interpreted by Jint;
    /// JS-level recursion costs roughly 10× more .NET stack frames than direct
    /// .NET recursion, so deeply nested input (e.g. 300+ chained ternaries)
    /// exhausts the thread stack and crashes the host process with an
    /// unrecoverable <see cref="StackOverflowException"/>. The pre-parse depth
    /// scan rejects such input with a controlled exception instead.
    /// </para>
    /// <para>
    /// 128 sits well above any hand-written predicate (typical depth &lt; 10)
    /// and ~2.3× below the empirical SOE threshold (~300). Bump only if you
    /// have a legitimate use case for deeper nesting and run on a sufficiently
    /// large stack.
    /// </para>
    /// </summary>
    public int MaxParseDepth { get; init; } = 128;

    /// <summary>
    /// Transpile TypeScript source code to JavaScript.
    ///
    /// Throws <see cref="TsTranspileException"/> if the TypeScript compiler
    /// reports any error, or if the source's nesting depth exceeds
    /// <see cref="MaxParseDepth"/>. Non-error diagnostics are dropped; use
    /// <see cref="TranspileWithSourceMap"/> if you need them.
    /// </summary>
    public string Transpile(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return "";
        EnforceMaxParseDepth(sourceCode);
        var (js, _, _) = TranspileCore(sourceCode, emitSourceMap: false);
        return js;
    }

    /// <summary>
    /// Transpile TypeScript source code and return the JavaScript output,
    /// the source map (Source Map v3 JSON), and any non-error diagnostics
    /// (warnings / suggestions / messages).
    ///
    /// Throws <see cref="TsTranspileException"/> on any error-category
    /// diagnostic, or if the source's nesting depth exceeds
    /// <see cref="MaxParseDepth"/> — same error contract as
    /// <see cref="Transpile(string)"/>.
    /// </summary>
    public TsTranspileResult TranspileWithSourceMap(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return new TsTranspileResult("", "", []);
        EnforceMaxParseDepth(sourceCode);
        var (js, map, warnings) = TranspileCore(sourceCode, emitSourceMap: true);
        return new TsTranspileResult(js, map ?? "", warnings);
    }

    private void EnforceMaxParseDepth(string sourceCode)
    {
        var depth = MeasureMaxNestingDepth(sourceCode);
        if (depth > MaxParseDepth)
        {
            var diag = new TsDiagnostic(
                TsDiagnosticCategory.Error,
                Code: 0,
                Message: $"Source nesting depth ({depth}) exceeds MaxParseDepth ({MaxParseDepth}). " +
                         "Refactor the script to use intermediate variables or shorter chains, " +
                         "or raise TsTranspiler.MaxParseDepth if you need deeper trees.",
                Line: 1,
                Column: 1);
            throw new TsTranspileException([diag]);
        }
    }

    /// <summary>
    /// Walks <paramref name="source"/> counting unmatched <c>(</c>, <c>[</c>,
    /// <c>{</c> opens and returns the maximum depth observed. Skips contents of
    /// single-quoted, double-quoted, and backtick string literals; skips line
    /// (<c>// …</c>) and block (<c>/* … */</c>) comments. The scan is deliberately
    /// simple — it does not parse template-literal <c>${…}</c> interpolations
    /// specially (their bracket counts are skipped along with the surrounding
    /// string), and it does not distinguish regex literals from division.
    /// Both omissions undercount slightly, which is the safe direction:
    /// legitimate scripts are not falsely rejected, and the deep-nesting attack
    /// vector (raw <c>(((…)))</c> chains) is not concealed by these omissions.
    /// </summary>
    internal static int MeasureMaxNestingDepth(string source)
    {
        int depth = 0, max = 0, len = source.Length;
        for (int i = 0; i < len;)
        {
            var c = source[i];

            // Comments
            if (c == '/' && i + 1 < len)
            {
                var next = source[i + 1];
                if (next == '/')
                {
                    i += 2;
                    while (i < len && source[i] != '\n') i++;
                    continue;
                }
                if (next == '*')
                {
                    i += 2;
                    while (i + 1 < len && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    i = i + 1 < len ? i + 2 : len;
                    continue;
                }
            }

            // String literals — single, double, backtick. Honour backslash-escapes.
            if (c == '\'' || c == '"' || c == '`')
            {
                var quote = c;
                i++;
                while (i < len && source[i] != quote)
                {
                    if (source[i] == '\\' && i + 1 < len) { i += 2; continue; }
                    i++;
                }
                if (i < len) i++; // consume closing quote
                continue;
            }

            switch (c)
            {
                case '(' or '[' or '{':
                    depth++;
                    if (depth > max) max = depth;
                    break;
                case ')' or ']' or '}':
                    if (depth > 0) depth--;
                    break;
            }
            i++;
        }
        return max;
    }

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
