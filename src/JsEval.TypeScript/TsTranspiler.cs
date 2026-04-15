using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
using Cocoar.JsEval;
using Jint;

namespace Cocoar.JsEval.TypeScript;

/// <summary>
/// Transpiles TypeScript source code to JavaScript (ESNext target).
/// Use this to compile TypeScript once (e.g., at save time), then execute
/// the resulting JavaScript with JavaScriptEngine.
/// </summary>
public sealed class TsTranspiler
{
    private static readonly ScriptParsingOptions ParsingOptions = new() { Tolerant = true };
    private static Prepared<Script>? _typeScriptScript;
    private static readonly ConcurrentBag<Engine> _enginePool = new();
    private static readonly Regex ClrConstructorRegex = new(@"new\s(?<typeName>[a-zA-Z0-9_\.\s<>\[\]$,]+)\((?<parameters>[a-zA-Z0-9_\.,\s<>\[\]$'""]+)?\)(;)?(?<ignore>//ignore)?", RegexOptions.Compiled);

    /// <summary>
    /// Transpile TypeScript source code to JavaScript.
    /// </summary>
    public string Transpile(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return "";

        sourceCode = RewriteClrConstructors(sourceCode);

        EnsureTypeScriptCompilerLoaded();

        var engine = RentEngine();
        try
        {
            engine.SetValue("src", sourceCode);

            var transpileOptions = """{"compilerOptions": {"target":"ESNext","module":"ESNext"}}""";
            var output = engine.Evaluate($"ts.transpileModule(src, {transpileOptions})", ParsingOptions).AsObject();
            return output.Get("outputText").AsString();
        }
        finally
        {
            ReturnEngine(engine);
        }
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
