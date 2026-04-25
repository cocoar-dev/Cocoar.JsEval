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

    // Short-name alias map (alias → Type), matching JsEngineOptions.TypeAliases.
    // When populated, the matched type is emitted at the root scope (no namespace
    // wrapper) under the alias, and cross-references elsewhere resolve to the
    // same short name.
    private Dictionary<string, Type> TypeAliases { get; } = new(StringComparer.Ordinal);

    // Ordered list of (sourcePrefix, targetPrefix) namespace mappings. Applied in
    // insertion order on first-match basis (so more specific prefixes should be
    // registered before broader ones).
    private List<(string Source, string Target)> NamespaceMappings { get; } = [];

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

    /// <summary>
    /// Register a type with an explicit short name. The type is emitted at the
    /// root scope (outside any <c>declare namespace</c> wrapper) under the alias,
    /// and every cross-reference to this type from other rendered members uses
    /// the short name too.
    /// <para>
    /// Aliases win over <see cref="MapNamespace(string, string)"/> when both match.
    /// </para>
    /// </summary>
    public DefinitionBuilder AddType(Type type, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias must be a non-empty identifier.", nameof(alias));
        AddTypes(type);
        var t = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        if (TypeAliases.TryGetValue(alias, out var existing) && existing != t)
        {
            throw new InvalidOperationException(
                $"Type alias '{alias}' is already assigned to '{existing.FullName}'. " +
                $"Cannot reassign it to '{t.FullName}'. " +
                $"Pick a different alias to disambiguate.");
        }
        TypeAliases[alias] = t;
        return this;
    }

    public DefinitionBuilder AddType<T>(string alias) => AddType(typeof(T), alias);

    /// <summary>
    /// Map a source namespace prefix to a target namespace prefix for rendering.
    /// Any type whose namespace starts with <paramref name="sourcePrefix"/> has
    /// that prefix replaced by <paramref name="targetPrefix"/> in the emitted
    /// <c>.d.ts</c>. An empty <paramref name="targetPrefix"/> flattens matched
    /// types to the root scope.
    /// <para>
    /// <c>System.*</c> types are excluded from mapping by default — they stay
    /// fully qualified. Cross-references to mapped types from other rendered
    /// members resolve to the mapped short form.
    /// </para>
    /// <para>
    /// Per-type <see cref="AddType(Type, string)"/> aliases win over namespace
    /// mappings when both match. If two distinct types resolve to the same
    /// (mapped-namespace, name) pair, <see cref="Render"/> throws.
    /// </para>
    /// </summary>
    public DefinitionBuilder MapNamespace(string sourcePrefix, string targetPrefix)
    {
        ArgumentNullException.ThrowIfNull(sourcePrefix);
        ArgumentNullException.ThrowIfNull(targetPrefix);
        NamespaceMappings.Add((sourcePrefix, targetPrefix));
        return this;
    }

    internal IReadOnlyDictionary<string, Type> GetTypeAliases() => TypeAliases;

    internal IReadOnlyList<(string Source, string Target)> GetNamespaceMappings() => NamespaceMappings;

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

    private readonly List<(Type BaseType, string Value, Type ConcreteType)> _discriminatorMappings = [];

    /// <summary>
    /// Registers discriminator mappings for a polymorphic base type. For each mapping
    /// TsDefinition emits an overloaded <c>Is(value: BaseType, d: 'value'): value is ConcreteType</c>
    /// signature on the global <c>declare const Type</c> object so Monaco can narrow
    /// the parameter type correctly after a <c>Type.Is(a, 'dog')</c> call.
    /// </summary>
    public DefinitionBuilder AddDiscriminatorMappings(
        Type baseType, params (string value, Type concreteType)[] mappings)
    {
        foreach (var (value, concreteType) in mappings)
        {
            AddTypes(baseType, concreteType);
            _discriminatorMappings.Add((baseType, value, concreteType));
        }
        return this;
    }

    /// <inheritdoc cref="AddDiscriminatorMappings(Type, ValueTuple{string, Type}[])"/>
    public DefinitionBuilder AddDiscriminatorMappings<TBase>(
        params (string value, Type concreteType)[] mappings)
        => AddDiscriminatorMappings(typeof(TBase), mappings);

    internal IReadOnlyList<(Type BaseType, string Value, Type ConcreteType)> GetDiscriminatorMappings()
        => _discriminatorMappings;

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
