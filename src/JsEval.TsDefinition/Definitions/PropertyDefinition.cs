using System;
using System.Reflection;
using System.Text;
using Cocoar.JsEval.TsDefinition.ExtensionMethods;

namespace Cocoar.JsEval.TsDefinition.Definitions;

public class PropertyDefinition : IDefinition
{
    public string Name { get; set; } = "";
    public TypeDefinition Type { get; set; } = null!;
    public string? AccessModifier { get; set; }
    public string? FromType { get; set; }
    public bool IsPublic { get; set; }
    public string? GetterModifer { get; set; }
    public string? SetterModifier { get; set; }

    public override string ToString()
    {
        var str = new StringBuilder();
        str.Append($"{AccessModifier} {Type.Name} {Name} {{");

        if (!string.IsNullOrWhiteSpace(GetterModifer))
            str.Append(GetterModifer != AccessModifier ? $" {GetterModifer} get;" : " get;");

        if (!string.IsNullOrWhiteSpace(SetterModifier))
            str.Append(SetterModifier != AccessModifier ? $" {SetterModifier} set;" : " set;");

        str.Append(" }");
        return str.ToString();
    }

    public static PropertyDefinition FromPropertyInfo(PropertyInfo propertyInfo)
    {
        var pDesc = new PropertyDefinition
        {
            Name = propertyInfo.Name,
            Type = TypeDefinition.FromType(propertyInfo.PropertyType),
            IsPublic = propertyInfo.IsPublic(),
            GetterModifer = propertyInfo.GetMethod is not null ? MethodDefinition.GetAccessModifier(propertyInfo.GetMethod.Attributes) : null,
            SetterModifier = propertyInfo.SetMethod is not null ? MethodDefinition.GetAccessModifier(propertyInfo.SetMethod.Attributes) : null,
        };

        if (propertyInfo.Name.Contains('.'))
        {
            var lastDot = pDesc.Name.LastIndexOf('.');
            pDesc.FromType = pDesc.Name[..lastDot];
            pDesc.Name = pDesc.Name[(lastDot + 1)..];
        }

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

        return pDesc;
    }
}
