using System.IO;
using System.Reflection;

namespace Cocoar.JsEval.Linq;

/// <summary>
/// Exposes the embedded TypeScript declaration file
/// (<c>cocoar-jseval-linq.d.ts</c>) that adds C# LINQ method aliases
/// (<c>Contains</c>, <c>Any</c>, <c>Where</c>, …) to the built-in
/// <see cref="string"/> and <see cref="Array"/> types via TypeScript
/// declaration merging.
///
/// <para>
/// Hosts can write this to disk (so Monaco / tsc picks it up automatically)
/// or embed the contents inline.
/// </para>
/// </summary>
public static class LinqTypeScriptDefinition
{
    private const string ResourceName = "Cocoar.JsEval.Linq.TypeScript.cocoar_jseval_linq.d.ts";

    /// <summary>Returns the raw TypeScript source as a string.</summary>
    public static string Read()
    {
        var asm = typeof(LinqTypeScriptDefinition).Assembly;
        // The embedded-resource name mirrors the folder path with dots.
        var name = ResolveResourceName(asm);
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded TypeScript definition not found. Expected resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Writes the definition to a file path (creates parent directories as needed).</summary>
    public static void WriteTo(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, Read());
    }

    private static string ResolveResourceName(Assembly asm)
    {
        // Folder "TypeScript" + file "cocoar-jseval-linq.d.ts" → the embedded name
        // preserves dashes/dots in the filename but replaces path separators with '.'.
        foreach (var candidate in asm.GetManifestResourceNames())
        {
            if (candidate.EndsWith("cocoar-jseval-linq.d.ts", StringComparison.Ordinal))
                return candidate;
        }
        throw new InvalidOperationException(
            $"Could not find 'cocoar-jseval-linq.d.ts' among embedded resources: " +
            string.Join(", ", asm.GetManifestResourceNames()));
    }
}
