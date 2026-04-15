using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cocoar.JsEval.TsDefinition.ExtensionMethods;

namespace Cocoar.JsEval.TsDefinition;

public class DefinitionBuilder
{
    private List<Type> TypesToProcess { get; set; } = [];
    private List<Type> CalculatedTypes { get; set; } = [];
    private List<MethodInfo> ExtensionMethods { get; set; } = [];

    public DefinitionBuilder AddTypes(params Type[] types) => AddTypes(types.AsEnumerable());

    public DefinitionBuilder AddTypes(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            var t = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (!TypesToProcess.Contains(t))
                TypesToProcess.Add(t);
        }
        return this;
    }

    public DefinitionBuilder AddType<T>() => AddTypes(typeof(T));

    public DefinitionBuilder AddExtensionMethods(params Type[] types) => AddExtensionMethods(types.AsEnumerable());

    public DefinitionBuilder AddExtensionMethods(IEnumerable<Type> types)
    {
        foreach (var type in types)
            AddExtensionMethods(type.GetExtensionMethods());
        return this;
    }

    public DefinitionBuilder AddExtensionMethods(IEnumerable<MethodInfo> methodInfos)
    {
        foreach (var methodInfo in methodInfos)
        {
            if (methodInfo.IsExtensionMethod() && !ExtensionMethods.Contains(methodInfo))
                ExtensionMethods.Add(methodInfo);
        }
        return this;
    }

    public DefinitionBuilder AddTypesFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t.IsPublic))
            AddTypes(type);
        return this;
    }

    internal List<Type> GetTypesToProcess()
    {
        foreach (var type in TypesToProcess)
            AddDependedTypes(type);
        return [.. CalculatedTypes];
    }

    internal List<MethodInfo> GetExtensionMethods() => [.. ExtensionMethods];

    private void AddDependedTypes(Type type)
    {
        if (type.IsGenericType)
        {
            foreach (var arg in type.GenericTypeArguments)
                AddDependedTypes(arg);
            type = type.GetGenericTypeDefinition();
        }

        if (CalculatedTypes.Contains(type))
            return;

        CalculatedTypes.Add(type);

        if (type.BaseType is not null)
            AddDependedTypes(type.BaseType);

        foreach (var propertyInfo in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            AddDependedTypes(propertyInfo.PropertyType);

        foreach (var interfaceType in type.GetInterfaces())
            AddDependedTypes(interfaceType);

        foreach (var methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            AddDependedTypes(methodInfo.ReturnType);
            foreach (var parameterInfo in methodInfo.GetParameters())
                AddDependedTypes(parameterInfo.ParameterType);
        }
    }

    public Dictionary<string, string> Render() => new TypeScriptRenderer().Render(this);
}
