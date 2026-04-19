using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Cocoar.JsEval.Linq;

/// <summary>
/// Contributes <c>linq.d.ts</c> (declaring the <c>linq</c> runtime global —
/// <c>linq.guid(…)</c>, <c>linq.decimal(…)</c>, …) to
/// <c>TsDefinitionService.GetTsDefinitions()</c>.
/// Registered automatically in DI by <c>JsEvalBuilder.AddLinq()</c>.
/// </summary>
internal sealed class LinqTsContributor : IJsTsDefinitionContributor
{
    private static readonly string _linqDts = LoadEmbedded("linq.d.ts");

    public IEnumerable<KeyValuePair<string, string>> GetTsDefinitions()
    {
        yield return new KeyValuePair<string, string>("linq.d.ts", _linqDts);
    }

    private static string LoadEmbedded(string logicalName)
    {
        var asm = typeof(LinqTsContributor).Assembly;
        using var stream = asm.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{logicalName}' not found in {asm.GetName().Name}. " +
                $"Available: {string.Join(", ", asm.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
