using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Cocoar.JsEval.TsDefinition.ExtensionMethods;

public static class TypeExtensions
{
    public static bool IsGenericTypeParameter(this Type type) =>
        type.IsGenericParameter && type.DeclaringMethod is null;

    public static string GetFriendlyName(this Type type)
    {
        var tdInfo = type.GetTypeInfo();
        var isGenericTypeParameter = tdInfo.IsGenericTypeParameter();
        var isArray = tdInfo.IsArray;

        if (isArray)
        {
            var arrElementInfo = tdInfo.GetElementType()?.GetTypeInfo();
            tdInfo = arrElementInfo!;
            if (arrElementInfo?.IsGenericTypeParameter() == true)
                isGenericTypeParameter = true;
        }

        var name = tdInfo.Name;
        string? genericTypesString = null;

        if (tdInfo.IsGenericType)
        {
            name = name.Contains('`') ? name[..name.IndexOf('`')] : name;
            var genericTypes = tdInfo.GenericTypeParameters.Length > 0
                ? tdInfo.GenericTypeParameters.Select(GetFriendlyName)
                : tdInfo.GenericTypeArguments.Select(GetFriendlyName);

            genericTypesString = $"<{string.Join(", ", genericTypes)}>";
        }

        name = isGenericTypeParameter || tdInfo.IsGenericParameter
            ? $"{name}{genericTypesString}"
            : $"{tdInfo.Namespace}.{name}{genericTypesString}";

        if (isArray)
            name = $"{name}[]";

        return name;
    }

    public static bool IsNullableType(this Type type) =>
        IsGenericTypeOf(type, typeof(Nullable<>));

    public static bool IsGenericTypeOf(this Type type, Type genericType) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == genericType;

    public static bool IsGenericTypeOf<T>(this Type type) =>
        IsGenericTypeOf(type, typeof(T));

    public static IEnumerable<MethodInfo> GetExtensionMethods(this Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.IsExtensionMethod());

    public static bool IsExtensionMethod(this MethodBase methodInfo) =>
        methodInfo.IsDefined(typeof(ExtensionAttribute), true);

    public static bool IsStatic(this Type type) =>
        type.IsAbstract && type.IsSealed;

    public static IEnumerable<Type> GetDirectInterfaces(this Type type)
    {
        var allInterfaces = type.GetInterfaces().ToList();
        var childInterfaces = new List<Type>();

        foreach (var i in allInterfaces)
            foreach (var ii in i.GetInterfaces())
                childInterfaces.Add(ii);

        if (type.BaseType is not null)
            foreach (var baseTypeInterface in type.BaseType.GetInterfaces())
                childInterfaces.Add(baseTypeInterface);

        return allInterfaces.Except(childInterfaces);
    }
}
