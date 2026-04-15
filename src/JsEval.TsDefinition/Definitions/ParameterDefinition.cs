using System.Reflection;
using Cocoar.JsEval.TsDefinition.ExtensionMethods;

namespace Cocoar.JsEval.TsDefinition.Definitions;

public class ParameterDefinition : IDefinition
{
    public string Name { get; set; } = "";
    public TypeDefinition Type { get; set; } = null!;
    public string? Ref { get; set; }
    public object? DefaultValue { get; set; }
    public bool IsOptional { get; set; }

    public override string ToString() => $"{Ref}{Type} {Name}";

    public static ParameterDefinition FromParameterInfo(ParameterInfo parameterInfo)
    {
        var pType = parameterInfo.ParameterType;
        string? refModifier = null;

        if (pType.IsByRef)
        {
            refModifier = parameterInfo.IsOut ? "out " : "ref ";
            pType = parameterInfo.ParameterType.GetElementType()!;
        }

        var type = pType.IsGenericType && pType.IsGenericTypeParameter() && !pType.IsArray
            ? TypeDefinition.FromType(pType.GetGenericTypeDefinition())
            : TypeDefinition.FromType(pType);

        return new ParameterDefinition
        {
            Name = parameterInfo.Name ?? "p",
            Type = type,
            Ref = refModifier,
            IsOptional = parameterInfo.IsOptional,
            DefaultValue = parameterInfo.DefaultValue
        };
    }
}
