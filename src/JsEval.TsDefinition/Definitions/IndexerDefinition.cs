using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cocoar.JsEval.TsDefinition.ExtensionMethods;

namespace Cocoar.JsEval.TsDefinition.Definitions;

public class IndexerDefinition : IDefinition
{
    public string Name { get; set; } = "";
    public string? AccessModifier { get; set; }
    public TypeDefinition ReturnType { get; set; } = null!;
    public List<ParameterDefinition> Parameters { get; set; } = [];
    public string? GetterModifer { get; set; }
    public string? SetterModifier { get; set; }

    public static IndexerDefinition FromPropertyInfo(PropertyInfo propertyInfo)
    {
        if (!propertyInfo.IsIndexerProperty())
            throw new InvalidCastException($"'{propertyInfo.Name}' is NOT an Indexer Property!");

        var pDesc = new IndexerDefinition
        {
            Name = propertyInfo.Name,
            ReturnType = propertyInfo.GetMethod is not null
                ? TypeDefinition.FromType(propertyInfo.GetMethod.ReturnType)
                : TypeDefinition.FromType(typeof(void)),
            GetterModifer = propertyInfo.GetMethod is not null ? MethodDefinition.GetAccessModifier(propertyInfo.GetMethod.Attributes) : null,
            SetterModifier = propertyInfo.SetMethod is not null ? MethodDefinition.GetAccessModifier(propertyInfo.SetMethod.Attributes) : null,
        };

        var hasGetter = !string.IsNullOrWhiteSpace(pDesc.GetterModifer);
        var hasSetter = !string.IsNullOrWhiteSpace(pDesc.SetterModifier);

        pDesc.AccessModifier = (hasGetter, hasSetter) switch
        {
            (true, true) when pDesc.GetterModifer == pDesc.SetterModifier => pDesc.GetterModifer,
            (true, true) => Array.IndexOf(Constants.AccessModifiers, pDesc.GetterModifer) <
                            Array.IndexOf(Constants.AccessModifiers, pDesc.SetterModifier)
                ? pDesc.GetterModifer : pDesc.SetterModifier,
            (true, false) => pDesc.GetterModifer,
            (false, true) => pDesc.SetterModifier,
            _ => null
        };

        if (hasGetter && propertyInfo.GetMethod is not null)
            pDesc.Parameters = propertyInfo.GetMethod.GetParameters().Select(ParameterDefinition.FromParameterInfo).ToList();
        else if (hasSetter && propertyInfo.SetMethod is not null)
            pDesc.Parameters = propertyInfo.SetMethod.GetParameters().Select(ParameterDefinition.FromParameterInfo).ToList();

        return pDesc;
    }
}
