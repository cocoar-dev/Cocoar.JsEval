using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cocoar.JsEval.TsDefinition.ExtensionMethods;

namespace Cocoar.JsEval.TsDefinition.Definitions;

public class TypeDefinition : IDefinition
{
    public string Name { get; set; } = "";
    public string? Namespace { get; set; }
    public string? AccessModifier { get; set; }
    public string? Kind { get; set; }

    public bool IsStatic { get; set; }
    public bool IsArray { get; set; }
    public bool IsGeneric { get; set; }
    public bool IsNullable { get; set; }

    public string? FriendlyName { get; private set; }
    public TypeDefinition? BaseType { get; set; }
    public List<TypeDefinition>? ImplementedInterfaces { get; set; }
    public List<TypeDefinition> GenericArguments { get; set; } = [];
    public List<EnumValueDefinition> EnumValueDefinitions { get; set; } = [];
    public Type? RawType { get; set; }
    public List<PropertyDefinition> Properties { get; set; } = [];
    public List<ConstructorDefinition> Constructors { get; set; } = [];
    public List<IndexerDefinition> Indexer { get; set; } = [];
    public List<MethodDefinition> Methods { get; set; } = [];

    private readonly Dictionary<string, object> _payload = [];

    public override string ToString()
    {
        var str = Name;
        if (GenericArguments.Count > 0)
            str += $"<{string.Join(", ", GenericArguments.Select(arg => arg.ToString()))}>";
        return str;
    }

    public T? GetPayload<T>() where T : class => GetPayload(typeof(T)) as T;
    public object? GetPayload(Type key) => GetPayload(key.FullName!);
    public object? GetPayload(string key) => _payload.GetValueOrDefault(key);

    public bool TryGetPayload<T>(out T? value) where T : class
    {
        if (TryGetPayload(typeof(T), out var v))
        {
            value = v as T;
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetPayload(Type key, out object? value) => TryGetPayload(key.FullName!, out value);
    public bool TryGetPayload(string key, out object? value) => _payload.TryGetValue(key, out value);

    public bool HasPayload<T>() where T : class => HasPayload(typeof(T));
    public bool HasPayload(Type key) => HasPayload(key.FullName!);
    public bool HasPayload(string key) => _payload.ContainsKey(key);

    public void SetPayload<T>(object value) where T : class => SetPayload(typeof(T), value);
    public void SetPayload(Type key, object value) => SetPayload(key.FullName!, value);
    public void SetPayload(string key, object value) => _payload[key] = value;

    public void RemovePayload<T>() where T : class => RemovePayload(typeof(T));
    public void RemovePayload(Type key) => RemovePayload(key.FullName!);
    public void RemovePayload(string key) => _payload.Remove(key);

    public static TypeDefinition FromType(Type type, IEnumerable<Type>? validTypes = null)
    {
        var isNullable = type.IsNullableType();

        if (isNullable)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
                type = underlying;
            else
                isNullable = false;
        }

        if (type.FullName?.EndsWith('&') == true || type.FullName?.EndsWith('*') == true)
        {
            var cleanedType = Type.GetType(type.FullName.TrimEnd('&', '*'));
            if (cleanedType is null)
                return TypeCache.JsAny;
            type = cleanedType;
        }

        var validTypesList = validTypes?.ToList();
        if (validTypesList is not null && !validTypesList.Contains(type))
        {
            if (!type.IsGenericType && !type.IsGenericTypeParameter())
                return TypeCache.JsAny;
        }

        var fName = type.GetFriendlyName();
        if (TypeCache.Cache.TryGetValue(fName, out var tDesc))
            return tDesc;

        tDesc = new TypeDefinition
        {
            RawType = type,
            IsNullable = isNullable,
            IsStatic = type.IsStatic(),
            FriendlyName = fName
        };

        TypeCache.Cache.TryAdd(fName, tDesc);

        var tdInfo = type.GetTypeInfo();
        var isGenericTypeParameter = tdInfo.IsGenericTypeParameter();
        tDesc.IsArray = tdInfo.IsArray;
        tDesc.IsGeneric = tdInfo.IsGenericTypeParameter() || tdInfo.IsGenericParameter;

        if (tDesc.IsArray)
        {
            var arrElementInfo = tdInfo.GetElementType()?.GetTypeInfo();
            tdInfo = arrElementInfo!;
            if (arrElementInfo?.IsGenericTypeParameter() == true)
                isGenericTypeParameter = true;
        }

        var name = tdInfo.Name;
        name = name.Contains('`') ? name[..name.IndexOf('`')] : name;

        if (!isGenericTypeParameter && !tdInfo.IsGenericParameter)
            tDesc.Namespace = tdInfo.Namespace;

        tDesc.Name = name;
        tDesc.AccessModifier = GetAccessModifier(tdInfo.Attributes);

        if (tdInfo.IsGenericType)
        {
            tDesc.GenericArguments = (tdInfo.GenericTypeParameters.Length > 0
                ? tdInfo.GenericTypeParameters
                : tdInfo.GenericTypeArguments)
                .Select(t => FromType(t, validTypesList)).ToList();
            tDesc.Name = $"{tDesc.Name}${tDesc.GenericArguments.Count}";
        }

        tDesc.Kind = tdInfo switch
        {
            { IsClass: true } => "class",
            { IsInterface: true } => "interface",
            { IsEnum: true } => "enum",
            { IsValueType: true } => "struct",
            _ => null
        };

        if (type.IsEnum && !tDesc.IsGeneric)
        {
            var fields = type.GetFields();
            var names = Enum.GetNames(type);
            tDesc.EnumValueDefinitions = names.Select(n => new EnumValueDefinition
            {
                Name = n,
                Value = Convert.ChangeType(Enum.Parse(type, n), fields.First().FieldType)
            }).ToList();
        }
        else
        {
            tDesc.Constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(ConstructorDefinition.FromConstructorInfo).ToList();

            tDesc.Properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => !p.IsIndexerProperty())
                .Select(PropertyDefinition.FromPropertyInfo).ToList();

            tDesc.Indexer = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .WhichIsIndexerProperty()
                .Select(IndexerDefinition.FromPropertyInfo).Where(p => !p.Name.Contains('.')).ToList();

            tDesc.Methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !m.Name.StartsWith('<'))
                .Select(MethodDefinition.FromMethodInfo).ToList();

            if (tdInfo.BaseType is not null)
                tDesc.BaseType = FromType(tdInfo.BaseType);

            tDesc.ImplementedInterfaces = tdInfo.GetInterfaces().Select(t => FromType(t, validTypesList)).ToList();
        }

        return tDesc;
    }

    private static string? GetAccessModifier(TypeAttributes attributes) => attributes switch
    {
        _ when (attributes & TypeAttributes.Public) == TypeAttributes.Public => "public",
        _ when (attributes & TypeAttributes.NestedFamORAssem) == TypeAttributes.NestedFamORAssem => "protected internal",
        _ when (attributes & TypeAttributes.NestedFamily) == TypeAttributes.NestedFamily => "protected",
        _ when (attributes & TypeAttributes.NestedAssembly) == TypeAttributes.NestedAssembly => "internal",
        _ when (attributes & TypeAttributes.NestedFamANDAssem) == TypeAttributes.NestedFamANDAssem => "private protected",
        _ when (attributes & TypeAttributes.NestedPrivate) == TypeAttributes.NestedPrivate => "private",
        _ => null
    };
}
