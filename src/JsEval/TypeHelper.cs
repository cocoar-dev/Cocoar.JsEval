using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.JsEval;

public static class TypeHelper
{
    private static readonly Dictionary<string, string> TypeMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["number"] = "double",
        ["any"] = "object",
        ["date"] = "System.DateTime"
    };

    private static readonly List<string> BuiltInTypeScriptType = ["date"];

    public static object? CreateObject(string typeName, object[] parameters)
    {
        var type = Cocoar.Reflectensions.Helper.TypeHelper.FindType(typeName, TypeMapping);
        if (type is null)
            return null;

        if (parameters?.Any() == true)
        {
            return Activator.CreateInstance(type, parameters);
        }
        else
        {
            return Activator.CreateInstance(type);
        }
    }

    public static object? CreateObjectWithDI(IServiceProvider serviceProvider, string typeName, object[] parameters)
    {
        var type = Cocoar.Reflectensions.Helper.TypeHelper.FindType(typeName, TypeMapping);
        if (type is null)
            return null;

        if (parameters?.Any() == true)
        {
            return ActivatorUtilities.CreateInstance(serviceProvider, type, parameters);
        }
        else
        {
            return ActivatorUtilities.CreateInstance(serviceProvider, type);
        }
    }

    public static Type? FindConstructorReplaceType(string typeName)
    {
        if (BuiltInTypeScriptType.Contains(typeName.ToLower()))
        {
            return null;
        }
        return Cocoar.Reflectensions.Helper.TypeHelper.FindType(typeName, TypeMapping);
    }
}
