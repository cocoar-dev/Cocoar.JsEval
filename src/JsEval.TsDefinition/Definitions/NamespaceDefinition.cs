using System.Collections.Generic;
using System.Linq;

namespace Cocoar.JsEval.TsDefinition.Definitions;

public class NamespaceDefinition : IDefinition
{
    public string Name { get; set; } = "";
    public List<NamespaceDefinition> Namespaces { get; set; } = [];
    public List<TypeDefinition> Types { get; set; } = [];

    public NamespaceDefinition? GetNameSpaceDefinition(string name)
    {
        var nsParts = name.Split('.');
        var current = this;

        foreach (var nsPart in nsParts)
        {
            var foundNs = current.Namespaces.FirstOrDefault(n => n.Name == nsPart);
            if (foundNs is null)
                return null;
            current = foundNs;
        }

        return current;
    }

    public NamespaceDefinition AddNamespaceDefinition(string name)
    {
        var nsParts = name.Split('.');
        var ns = this;

        foreach (var nsPart in nsParts)
        {
            var foundNs = ns.Namespaces.FirstOrDefault(n => n.Name == nsPart);
            if (foundNs is null)
            {
                foundNs = new NamespaceDefinition { Name = nsPart };
                ns.Namespaces.Add(foundNs);
            }
            ns = foundNs;
        }

        return ns;
    }
}
