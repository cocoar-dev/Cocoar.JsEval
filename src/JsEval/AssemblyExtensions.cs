using System.IO;
using System.Reflection;

namespace Cocoar.JsEval;

public static class AssemblyExtensions
{
    public static string? ReadResourceAsString(this Assembly assembly, string resourceName)
    {
        var fullName = FindResourceName(assembly, resourceName);
        if (fullName is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(fullName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? FindResourceName(Assembly assembly, string partialName)
    {
        var names = assembly.GetManifestResourceNames();
        foreach (var name in names)
        {
            if (name.EndsWith(partialName) || name.Contains($".{partialName}"))
            {
                return name;
            }
        }
        return null;
    }
}
