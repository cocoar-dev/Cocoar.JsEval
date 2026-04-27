using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Cocoar.JsEval.TsDefinition.ExtensionMethods;

public static class PropertyInfoExtensionMethods
{
    public static bool IsIndexerProperty(this PropertyInfo propertyInfo)
    {
        var mName = propertyInfo.DeclaringType?.GetCustomAttribute<DefaultMemberAttribute>()?.MemberName ?? "Item";

        return propertyInfo.GetMethod?.Name.Equals($"get_{mName}", StringComparison.Ordinal) == true ||
               propertyInfo.SetMethod?.Name.Equals($"set_{mName}", StringComparison.Ordinal) == true ||
               propertyInfo.GetMethod?.Name.EndsWith($".get_{mName}", StringComparison.Ordinal) == true ||
               propertyInfo.SetMethod?.Name.EndsWith($".set_{mName}", StringComparison.Ordinal) == true;
    }

    public static IEnumerable<PropertyInfo> WhichIsIndexerProperty(this IEnumerable<PropertyInfo> propertyInfos) =>
        propertyInfos.Where(IsIndexerProperty);

    public static bool IsPublic(this PropertyInfo propertyInfo) =>
        (propertyInfo.GetGetMethod(true)?.IsPublic ?? false) ||
        (propertyInfo.GetSetMethod(true)?.IsPublic ?? false);
}
