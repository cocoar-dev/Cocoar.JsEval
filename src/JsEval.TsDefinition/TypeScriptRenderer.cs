using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Cocoar.JsEval.TsDefinition.Definitions;

namespace Cocoar.JsEval.TsDefinition;

public class TypeScriptRenderer
{
    private readonly TypeScriptRendererDefaults Defaults = new();
    private List<Type> AllowedTypes = [];

    private static string? BuildDocComments(int indent, params string[] lines) =>
        BuildDocComments(indent, lines.AsEnumerable());

    private static string? BuildDocComments(int indent, IEnumerable<string> lines)
    {
        var linesList = lines.ToList();
        if (linesList.Count == 0)
            return null;

        var indentString = GetIndentString(indent);
        var comments = new StringBuilder();
        comments.AppendLine();
        comments.AppendLine($"{indentString}/**");
        foreach (var s in linesList)
            comments.AppendLine($"{indentString}* {s}");
        comments.AppendLine($"{indentString}*/");
        return comments.ToString();
    }

    private static string GetIndentString(int indent = 0) => new(' ', indent);

    public string Render(PropertyDefinition propertyDefinition, int indent)
    {
        var prop = BuildTypeString(propertyDefinition.Type);
        var otherType = GetTypeString(propertyDefinition.Type);

        var comments = prop != otherType ? BuildDocComments(indent, otherType) : null;

        var name = propertyDefinition.Name.Contains('.')
            ? propertyDefinition.Name.Split('.').Last()
            : propertyDefinition.Name;
        var priv = propertyDefinition.IsPublic ? "" : "private ";

        return $"{comments}{GetIndentString(indent)}{priv}{name}: {prop};";
    }

    public string Render(ConstructorDefinition constructorDefinition, int indent)
    {
        var parameters = new List<string>();
        var commentLines = new List<string>();

        foreach (var parameter in constructorDefinition.Parameters)
        {
            var paramName = Defaults.NormalizeIdentifier(parameter.Name);
            var buildType = BuildTypeString(parameter.Type);
            if (parameter.Type.IsArray && !buildType.EndsWith("[]"))
                buildType += "[]";

            var optional = parameter.IsOptional ? "?" : null;
            parameters.Add($"{paramName}{optional}: {buildType}");

            if (IsDelegateType(parameter.Type.RawType))
                continue;

            var getType = GetTypeString(parameter.Type);
            if (buildType != getType || parameter.IsOptional)
            {
                var defaultValue = parameter.IsOptional ? $" = {FormatDefaultValue(parameter)}" : null;
                commentLines.Add($"@param {paramName} {getType}{defaultValue}");
            }
        }

        var comments = BuildDocComments(indent, commentLines);
        return $"{comments}{GetIndentString(indent)}{constructorDefinition.Name}({string.Join(", ", parameters)});";
    }

    public string Render(IndexerDefinition indexerDefinition, int indent)
    {
        var parameters = new List<string>();
        var commentLines = new List<string>();

        var returnType = BuildTypeString(indexerDefinition.ReturnType);
        var otherReturnType = GetTypeString(indexerDefinition.ReturnType);

        foreach (var param in indexerDefinition.Parameters)
        {
            var buildType = BuildTypeString(param.Type);
            parameters.Add($"{param.Name}: {buildType}");

            var getType = GetTypeString(param.Type);
            if (buildType != getType)
                commentLines.Add($"@param {param.Name} {getType}");
        }

        if (returnType != otherReturnType)
            commentLines.Add($"@returns {otherReturnType}");

        var comments = BuildDocComments(indent, commentLines);
        return $"{comments}{GetIndentString(indent)}[{string.Join(", ", parameters)}]: {returnType};";
    }

    public string Render(MethodDefinition methodDefinition, int indent)
    {
        var parameters = new List<string>();
        var commentLines = new List<string>();

        var returnType = BuildTypeString(methodDefinition.ReturnType);
        if (methodDefinition.ReturnType.IsArray && !returnType.EndsWith("[]"))
            returnType += "[]";

        var otherReturnType = GetTypeString(methodDefinition.ReturnType);
        if (methodDefinition.ReturnType.IsArray && !otherReturnType.EndsWith("[]"))
            otherReturnType += "[]";

        foreach (var param in methodDefinition.Parameters)
        {
            var paramName = Defaults.NormalizeIdentifier(param.Name);
            var buildType = BuildTypeString(param.Type);
            if (param.Type.IsArray && !buildType.EndsWith("[]"))
                buildType += "[]";

            var optional = param.IsOptional ? "?" : null;
            parameters.Add($"{paramName}{optional}: {buildType}");

            if (IsDelegateType(param.Type.RawType))
                continue;

            var getType = GetTypeString(param.Type);
            if (buildType != getType || param.IsOptional)
            {
                var defaultValue = param.IsOptional ? $" = {FormatDefaultValue(param)}" : null;
                commentLines.Add($"@param {paramName} {getType}{defaultValue}");
            }
        }

        if (returnType != otherReturnType)
            commentLines.Add($"@returns {otherReturnType}");

        var comments = BuildDocComments(indent, commentLines);
        var genericArguments = methodDefinition.GenericArguments.Count > 0
            ? $"<{string.Join(", ", methodDefinition.GenericArguments)}>"
            : "";

        return $"{comments}{GetIndentString(indent)}{methodDefinition.Name}{genericArguments}({string.Join(", ", parameters)}): {returnType};";
    }

    public string Render(TypeDefinition typeDefinition, int indent = 0)
    {
        var strb = new StringBuilder();
        var kind = typeDefinition.Kind switch
        {
            "class" => "class",
            "enum" => "enum",
            _ => "interface"
        };

        var indentString = GetIndentString(indent);
        strb.Append($"{indentString}{kind} {BuildTypeDefinitionTypeString(typeDefinition)}");

        var extends = "";
        if (typeDefinition.BaseType is { RawType: not null })
        {
            var tdInfo = typeDefinition.BaseType.RawType.GetTypeInfo();
            var checkType = tdInfo.IsGenericType ? tdInfo.GetGenericTypeDefinition() : (Type)tdInfo;

            if (checkType != typeof(object) && checkType != typeof(Enum) &&
                checkType != typeof(ValueType) && AllowedTypes.Contains(checkType))
            {
                extends = $" extends {BuildTypeString(typeDefinition.BaseType)}";
            }
        }

        if (typeDefinition.ImplementedInterfaces?.Count > 0)
        {
            var impl = kind == "interface" ? "extends" : "implements";
            var names = typeDefinition.ImplementedInterfaces
                .Select(i => BuildTypeString(i))
                .Where(n => n is not "any" and not "any[]")
                .ToList();
            if (names.Count > 0)
                extends += $" {impl} {string.Join(", ", names)}";
        }

        strb.Append(extends);
        strb.AppendLine(" {");

        // Add missing interface methods
        var missingDefinitions = new TypeDefinition();
        if (typeDefinition.ImplementedInterfaces is not null)
        {
            foreach (var interf in typeDefinition.ImplementedInterfaces)
            {
                foreach (var method in interf.Methods)
                {
                    if (!typeDefinition.Methods.Any(m => m.Name == method.Name))
                        missingDefinitions.Methods.Add(method);
                }
            }
        }

        strb.AppendLine(RenderBody(typeDefinition, indent + 4));
        strb.AppendLine(RenderBody(missingDefinitions, indent + 4));
        strb.AppendLine($"{indentString}}}");
        return strb.ToString();
    }

    public string RenderBody(TypeDefinition typeDefinition, int indent = 0, Func<IDefinition, string, string>? definitionString = null)
    {
        var strb = new StringBuilder();

        if (typeDefinition.EnumValueDefinitions.Count > 0)
        {
            var enumLines = typeDefinition.EnumValueDefinitions
                .Select(ev => $"{GetIndentString(indent)}{ev.Name} = {ev.Value}");
            strb.AppendLine();
            strb.AppendLine(string.Join($",{Environment.NewLine}", enumLines));
        }

        if (typeDefinition.Properties.Count > 0)
        {
            strb.AppendLine();
            var processed = new HashSet<string>();
            foreach (var prop in typeDefinition.Properties.OrderByDescending(p => p.IsPublic))
            {
                if (!processed.Add(prop.Name)) continue;
                if (!prop.IsPublic && string.IsNullOrEmpty(prop.FromType)) continue;

                var rendered = Render(prop, indent);
                if (definitionString is not null)
                    rendered = definitionString(prop, rendered);
                if (!string.IsNullOrWhiteSpace(rendered))
                    strb.AppendLine(rendered);
            }
        }

        if (typeDefinition.Constructors.Count > 0)
        {
            strb.AppendLine();
            foreach (var ctor in typeDefinition.Constructors)
            {
                var rendered = Render(ctor, indent);
                if (definitionString is not null)
                    rendered = definitionString(ctor, rendered);
                if (!string.IsNullOrWhiteSpace(rendered))
                    strb.AppendLine(rendered);
            }
        }

        if (typeDefinition.Indexer.Count > 0)
        {
            strb.AppendLine();
            foreach (var idx in typeDefinition.Indexer)
            {
                var rendered = Render(idx, indent);
                if (definitionString is not null)
                    rendered = definitionString(idx, rendered);
                if (!string.IsNullOrWhiteSpace(rendered))
                    strb.AppendLine(rendered);
            }
        }

        if (typeDefinition.Methods.Count > 0)
        {
            strb.AppendLine();
            foreach (var method in typeDefinition.Methods)
            {
                if (method.GenericArguments.Count > 0 && !Defaults.IncludeGenericMethods)
                    continue;
                if (method.Parameters.Any(p => !string.IsNullOrWhiteSpace(p.Ref)) && !Defaults.IncludeMethodsWithReferenceParameters)
                    continue;

                var rendered = Render(method, indent);
                if (definitionString is not null)
                    rendered = definitionString(method, rendered);
                if (!string.IsNullOrWhiteSpace(rendered))
                    strb.AppendLine(rendered);
            }
        }

        return strb.ToString();
    }

    private string BuildTypeString(TypeDefinition typeDefinition, bool includeNamespace = true)
    {
        if (typeDefinition.TryGetPayload<NormalizedNonGenericTypeName>(out var normalized))
            return includeNamespace ? $"{normalized!.Namespace}.{normalized.TypeName}" : normalized!.TypeName;

        if (typeDefinition.FriendlyName == "System.Action" || typeDefinition.FriendlyName?.StartsWith("System.Action<") == true)
        {
            var args = typeDefinition.GenericArguments.Select(s => BuildTypeString(s)).ToList();
            return TypeCache.BuildActionTypeName(typeDefinition.RawType!, args);
        }

        if (typeDefinition.FriendlyName?.StartsWith("System.Func<") == true)
        {
            var args = typeDefinition.GenericArguments.Select(s => BuildTypeString(s)).ToList();
            return TypeCache.BuildFuncTypeName(typeDefinition.RawType!, args);
        }

        if (typeDefinition.FriendlyName?.StartsWith("System.Predicate<") == true)
        {
            var args = typeDefinition.GenericArguments.Select(s => BuildTypeString(s)).ToList();
            return TypeCache.BuildPredicateTypeName(typeDefinition.RawType!, args);
        }

        var name = Defaults.NormalizeTypeName(typeDefinition, AllowedTypes, includeNamespace);

        if (typeDefinition.IsNullable)
            return $"({name} | null)";

        if (name is not "any" and not "any[]" && typeDefinition.GenericArguments.Count > 0)
        {
            if (name.EndsWith("[]"))
                name = name[..^2];

            name += $"<{string.Join(", ", typeDefinition.GenericArguments.Select(s => BuildTypeString(s)))}>";

            if (typeDefinition.IsArray)
                name = $"{name}[]";
        }

        return name;
    }

    private string GetTypeString(TypeDefinition typeDefinition, bool includeNamespace = true)
    {
        var name = Defaults.NormalizeTypeName(typeDefinition, null!, includeNamespace);

        if (typeDefinition.IsNullable)
            return $"{name}?";

        if (typeDefinition.GenericArguments.Count > 0)
        {
            if (name.EndsWith("[]"))
                name = name[..^2];

            name += $"<{string.Join(", ", typeDefinition.GenericArguments.Select(s => GetTypeString(s)))}>";

            if (typeDefinition.IsArray)
                name = $"{name}[]";
        }

        return name;
    }

    private string BuildTypeDefinitionTypeString(TypeDefinition typeDefinition)
    {
        if (typeDefinition.TryGetPayload<NormalizedNonGenericTypeName>(out var normalized))
            return normalized!.TypeName;

        if (typeDefinition.FriendlyName == "System.Action" || typeDefinition.FriendlyName?.StartsWith("System.Action<") == true)
            return TypeCache.BuildActionTypeName(typeDefinition.RawType!, typeDefinition.GenericArguments.Select(s => BuildTypeString(s)).ToList());

        if (typeDefinition.FriendlyName?.StartsWith("System.Func<") == true)
            return TypeCache.BuildFuncTypeName(typeDefinition.RawType!, typeDefinition.GenericArguments.Select(s => BuildTypeString(s)).ToList());

        if (typeDefinition.FriendlyName?.StartsWith("System.Predicate<") == true)
            return TypeCache.BuildPredicateTypeName(typeDefinition.RawType!, typeDefinition.GenericArguments.Select(s => BuildTypeString(s)).ToList());

        var name = Defaults.NormalizeTypeName(typeDefinition, AllowedTypes, false);

        if (name is not "any" and not "any[]" && typeDefinition.GenericArguments.Count > 0)
        {
            if (name.EndsWith("[]"))
                name = name[..^2];

            name += $"<{string.Join(", ", typeDefinition.GenericArguments.Select(s => BuildTypeString(s)))}>";

            if (typeDefinition.IsArray)
                name = $"{name}[]";
        }

        return name;
    }

    private NormalizedNonGenericTypeName? GetNormalizedNonGenericTypeName(TypeDefinition typeDefinition)
    {
        if (typeDefinition.FriendlyName == "System.Action" ||
            typeDefinition.FriendlyName?.StartsWith("System.Action<") == true ||
            typeDefinition.FriendlyName?.StartsWith("System.Func<") == true ||
            typeDefinition.FriendlyName?.StartsWith("System.Predicate<") == true)
            return null;

        if (typeDefinition.GenericArguments.All(s => s.IsGeneric))
            return null;

        var name = Defaults.NormalizeTypeName(typeDefinition, AllowedTypes, false);

        if (name is not "any" and not "any[]" && typeDefinition.GenericArguments.Count > 0)
        {
            if (name.EndsWith("[]"))
                name = name[..^2];

            var genArgs = typeDefinition.GenericArguments.ToList();
            var nonGenericArgs = typeDefinition.GenericArguments.Where(arg => !arg.IsGeneric);
            genArgs.RemoveAll(arg => !arg.IsGeneric);

            foreach (var nonGenericArg in nonGenericArgs)
                name += $"$${BuildTypeString(nonGenericArg).Replace(".", "$")}";

            if (genArgs.Count > 0)
                name += $"<{string.Join(", ", genArgs.Select(s => BuildTypeString(s)))}>";

            if (typeDefinition.IsArray)
                name = $"{name}[]";
        }

        return new NormalizedNonGenericTypeName(name, typeDefinition.Namespace);
    }

    public Dictionary<string, string> Render(DefinitionBuilder definitionBuilder)
    {
        var namespaceDefinition = new NamespaceDefinition();
        AllowedTypes = definitionBuilder.GetTypesToProcess();

        foreach (var calculatedType in AllowedTypes)
        {
            var tdesc = TypeDefinition.FromType(calculatedType, AllowedTypes);
            var gn = GetNormalizedNonGenericTypeName(tdesc);
            if (gn is not null)
                tdesc.SetPayload<NormalizedNonGenericTypeName>(gn);

            if (tdesc.IsGeneric || tdesc.IsArray || tdesc.RawType == typeof(char))
                continue;

            var typeString = BuildTypeString(tdesc);
            if (!typeString.Contains('.') || typeString.EndsWith('&') || typeString.EndsWith('*'))
                continue;

            if (!string.IsNullOrWhiteSpace(tdesc.Namespace))
            {
                var ns = namespaceDefinition.AddNamespaceDefinition(tdesc.Namespace);
                ns.Types.Add(tdesc);
            }
        }

        // Process extension methods
        foreach (var methodInfo in definitionBuilder.GetExtensionMethods())
        {
            var mi = MethodDefinition.FromMethodInfo(methodInfo);

            if (mi.IsExtensionMethodFor?.Name?.Equals("string", StringComparison.OrdinalIgnoreCase) == true)
            {
                TypeCache.JsString.Methods.Add(mi);
            }
            else if (mi.IsExtensionMethodFor is not null)
            {
                var ns = namespaceDefinition.GetNameSpaceDefinition(mi.IsExtensionMethodFor.Namespace!) ??
                         namespaceDefinition.AddNamespaceDefinition(mi.IsExtensionMethodFor.Namespace!);

                if (!mi.IsExtensionMethodFor.HasPayload<NormalizedNonGenericTypeName>())
                {
                    var normalizedTypeName = GetNormalizedNonGenericTypeName(mi.IsExtensionMethodFor);
                    if (normalizedTypeName is not null)
                        mi.IsExtensionMethodFor.SetPayload<NormalizedNonGenericTypeName>(normalizedTypeName);
                }

                var existingType = ns.Types.FirstOrDefault(t => t == mi.IsExtensionMethodFor);
                if (existingType is null)
                {
                    ns.Types.Add(mi.IsExtensionMethodFor);
                    existingType = mi.IsExtensionMethodFor;
                }

                existingType.Methods.Add(mi);
            }
        }

        var dict = new Dictionary<string, string>();

        foreach (var definition in namespaceDefinition.Namespaces.OrderBy(n => n.Name))
            dict.Add($"{definition.Name}.d.ts", Render(definition));

        dict.Add("extensions.d.ts", RenderBuiltInExtensions());

        return dict;
    }

    private string RenderBuiltInExtensions()
    {
        var definitions = new List<string>();
        if (TypeCache.JsString.Methods.Count > 0)
            definitions.Add(Render(TypeCache.JsString));
        return string.Join(Environment.NewLine, definitions);
    }

    public string Render(NamespaceDefinition namespaceDefinition, int indent = 0)
    {
        var indentString = GetIndentString(indent);
        var strbuilder = new StringBuilder();

        if (indent == 0)
            strbuilder.Append("declare ");

        strbuilder.AppendLine($"{indentString}namespace {namespaceDefinition.Name} {{");
        indent += 4;

        strbuilder.AppendLine();
        foreach (var type in namespaceDefinition.Types.OrderBy(t => t.Name))
        {
            if (type.IsStatic || type.IsGeneric || type.Name.EndsWith('&'))
                continue;
            strbuilder.AppendLine(Render(type, indent));
        }

        foreach (var ns in namespaceDefinition.Namespaces.OrderBy(n => n.Name))
            strbuilder.AppendLine(Render(ns, indent));

        strbuilder.AppendLine($"{indentString}}}");
        return strbuilder.ToString();
    }

    private static bool IsDelegateType(Type? type) =>
        type?.Name is "Action" or not null &&
        (type.Name == "Action" || type.Name.StartsWith("Action`") ||
         type.Name.StartsWith("Func`") || type.Name.StartsWith("Predicate`"));

    private static string FormatDefaultValue(ParameterDefinition parameter)
    {
        if (parameter.DefaultValue is null)
            return "null";

        return parameter.DefaultValue switch
        {
            Enum e => $"{parameter.Type.FriendlyName}.{e}",
            bool b => b.ToString().ToLower(),
            _ => parameter.DefaultValue.ToString() ?? "null"
        };
    }
}

internal record NormalizedNonGenericTypeName(string TypeName, string? Namespace);
