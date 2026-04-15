using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Cocoar.JsEval.TsDefinition.Definitions;
using Cocoar.JsEval.TsDefinition.ExtensionMethods;

namespace Cocoar.JsEval.TsDefinition;

public class TypeScriptRendererDefaults
{
    public Dictionary<Type, string> TypeMappings { get; } = new()
    {
        [typeof(char)] = "string",
        [typeof(string)] = "string",
        [typeof(int)] = "number",
        [typeof(void)] = "void",
        [typeof(object)] = "any",
        [typeof(bool)] = "boolean",
        [typeof(DateTime)] = "Date",
        [typeof(DateTimeOffset)] = "Date",
        [typeof(Guid)] = "string",
        [typeof(TimeSpan)] = "number",
        [typeof(byte)] = "number",
        [typeof(byte[])] = "ArrayBuffer",
        [typeof(Task)] = "Promise<void>",
        [typeof(ValueTask)] = "Promise<void>",
    };

    /// <summary>
    /// Generic type mappings for types that are ACTUALLY converted at runtime.
    /// Only Task/ValueTask → Promise (via TaskInterop).
    /// Collections/Dictionaries are NOT mapped here — they stay as their .NET types
    /// with full IntelliSense, because Jint exposes them as ObjectWrappers, not native JS types.
    /// </summary>
    public Dictionary<Type, string> GenericTypeMappings { get; } = new()
    {
        [typeof(Task<>)] = "Promise",
        [typeof(ValueTask<>)] = "Promise",
    };

    public string NormalizeTypeName(TypeDefinition typeDefinition, List<Type> allowedTypes, bool includeNamespace = true)
    {
        if (typeDefinition.RawType is null)
            return typeDefinition.Name;

        var type = typeDefinition.RawType;

        // Direct type mapping takes priority (e.g., byte[] → ArrayBuffer before array decomposition)
        if (TypeMappings.TryGetValue(type, out var directMapping))
            return directMapping;

        // Handle generic type mappings (Task<T> → Promise<T>)
        if (type.IsGenericType && GenericTypeMappings.TryGetValue(type.GetGenericTypeDefinition(), out var genericWrapper))
        {
            var genericArgs = type.GetGenericArguments();
            var mappedArgs = genericArgs.Select(t =>
            {
                var def = TypeDefinition.FromType(t);
                return NormalizeTypeName(def, allowedTypes, includeNamespace);
            });
            return $"{genericWrapper}<{string.Join(", ", mappedArgs)}>";
        }

        if (typeDefinition.IsArray)
            type = type.GetElementType() ?? type;

        if (type.IsGenericTypeParameter() || type.IsGenericMethodParameter)
            return typeDefinition.Name;

        if (type == typeof(char))
            type = typeof(string);

        if (Constants.NumericTypes.Contains(type))
            type = typeof(int);

        if (!TypeMappings.TryGetValue(type, out var name))
        {
            var isAllowed = true;
            if (allowedTypes?.Any() == true)
            {
                var checkType = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
                if (!allowedTypes.Contains(checkType))
                    isAllowed = false;
            }

            if (isAllowed || typeDefinition.IsGeneric)
            {
                name = includeNamespace
                    ? $"{typeDefinition.Namespace}.{typeDefinition.Name}".Trim('.')
                    : typeDefinition.Name;
            }
            else
            {
                name = "any";
            }
        }

        if (typeDefinition.IsArray)
            name = $"{name}[]";

        return name;
    }

    public string NormalizeIdentifier(string value) =>
        ReservedWordsDictionary.TryGetValue(value, out var replacement) ? replacement : value;

    public Dictionary<string, string> ReservedWordsDictionary { get; } = new()
    {
        ["finally"] = "finaly",
        ["delete"] = "del",
        ["import"] = "imp",
        ["export"] = "exp",
        ["default"] = "def",
        ["class"] = "cls",
        ["function"] = "func",
    };

    public bool IncludeGenericMethods { get; set; }
    public bool IncludeMethodsWithReferenceParameters { get; set; }
}
