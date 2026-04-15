using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Cocoar.JsEval.TsDefinition.Definitions;

public class ConstructorDefinition : IDefinition
{
    public string Name { get; } = "constructor";
    public TypeDefinition? DeclaringType { get; set; }
    public List<TypeDefinition> GenericArguments { get; set; } = [];
    public List<ParameterDefinition> Parameters { get; set; } = [];

    public override string ToString() => $"{Name}({string.Join(", ", Parameters)})";

    public static ConstructorDefinition FromConstructorInfo(ConstructorInfo constructorInfo)
    {
        var cd = new ConstructorDefinition
        {
            DeclaringType = TypeDefinition.FromType(constructorInfo.DeclaringType!),
            Parameters = constructorInfo.GetParameters().Select(ParameterDefinition.FromParameterInfo).ToList(),
        };

        if (constructorInfo.IsGenericMethod)
            cd.GenericArguments = constructorInfo.GetGenericArguments().Select(t => TypeDefinition.FromType(t)).ToList();

        return cd;
    }
}
