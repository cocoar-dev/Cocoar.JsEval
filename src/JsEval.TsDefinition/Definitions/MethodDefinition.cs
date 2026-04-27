using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Cocoar.JsEval.TsDefinition.ExtensionMethods;

namespace Cocoar.JsEval.TsDefinition.Definitions;

public class MethodDefinition : IDefinition
{
    public string Name { get; set; } = "";
    public TypeDefinition ReturnType { get; set; } = null!;
    public string? AccessModifier { get; set; }
    public bool IsPublic { get; set; }
    public string? FromType { get; set; }
    public TypeDefinition? DeclaringType { get; set; }
    public TypeDefinition? IsExtensionMethodFor { get; set; }
    public List<TypeDefinition> GenericArguments { get; set; } = [];
    public List<ParameterDefinition> Parameters { get; set; } = [];

    public override string ToString()
    {
        var strb = new StringBuilder();
        strb.Append(CultureInfo.InvariantCulture, $"{AccessModifier} {ReturnType} {Name}");
        if (GenericArguments.Count > 0)
            strb.Append(CultureInfo.InvariantCulture, $"<{string.Join(", ", GenericArguments)}>");
        strb.Append(CultureInfo.InvariantCulture, $"({string.Join(", ", Parameters)})");
        return strb.ToString();
    }

    public static MethodDefinition FromMethodInfo(MethodInfo methodInfo)
    {
        var md = new MethodDefinition
        {
            Name = methodInfo.Name,
            AccessModifier = GetAccessModifier(methodInfo.Attributes),
            DeclaringType = TypeDefinition.FromType(methodInfo.DeclaringType!),
        };

        if (md.Name.Contains('.'))
        {
            var lastDot = md.Name.LastIndexOf('.');
            md.FromType = md.Name[..lastDot];
            md.Name = md.Name[(lastDot + 1)..];
        }

        var parameters = methodInfo.GetParameters();

        if (methodInfo.IsExtensionMethod())
        {
            md.Parameters = parameters.Skip(1).Select(ParameterDefinition.FromParameterInfo).ToList();
            md.IsExtensionMethodFor = TypeDefinition.FromType(parameters.First().ParameterType);
        }
        else
        {
            md.Parameters = parameters.Select(ParameterDefinition.FromParameterInfo).ToList();
        }

        md.ReturnType = TypeDefinition.FromType(methodInfo.ReturnType);

        if (methodInfo.IsGenericMethod)
        {
            var args = methodInfo.GetGenericArguments().Select(t => TypeDefinition.FromType(t)).ToList();
            if (md.IsExtensionMethodFor?.IsGeneric == true)
                args = args.Where(arg => arg.Name != md.IsExtensionMethodFor.Name).ToList();
            md.GenericArguments = args;
        }

        return md;
    }

    internal static string? GetAccessModifier(MethodAttributes attributes) => attributes switch
    {
        _ when (attributes & MethodAttributes.Public) == MethodAttributes.Public => "public",
        _ when (attributes & MethodAttributes.FamORAssem) == MethodAttributes.FamORAssem => "protected internal",
        _ when (attributes & MethodAttributes.Family) == MethodAttributes.Family => "protected",
        _ when (attributes & MethodAttributes.Assembly) == MethodAttributes.Assembly => "internal",
        _ when (attributes & MethodAttributes.FamANDAssem) == MethodAttributes.FamANDAssem => "private protected",
        _ when (attributes & MethodAttributes.Private) == MethodAttributes.Private => "private",
        _ => null
    };
}
