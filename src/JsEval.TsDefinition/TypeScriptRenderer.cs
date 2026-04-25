using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.TsDefinition.Definitions;

namespace Cocoar.JsEval.TsDefinition;

public class TypeScriptRenderer
{
    private readonly TypeScriptRendererDefaults Defaults = new();
    private List<Type> AllowedTypes = [];

    // Short-name map: Type → (mappedNamespace, shortName). Populated once in
    // Render(DefinitionBuilder) from TypeAliasResolver.Resolve() output. Used by
    // BuildTypeString / GetTypeString / BuildTypeDefinitionTypeString to rewrite
    // cross-references, and by the main render loop to place types into the
    // correct namespace bucket (including the empty "root" bucket).
    private Dictionary<Type, (string MappedNamespace, string ShortName)> _resolvedNames = new();

    // Types whose MappedNamespace is empty — emitted at module scope with a
    // 'declare' modifier, no namespace wrapper.
    private readonly List<TypeDefinition> _rootScopeTypes = new();

    // Discriminator mappings grouped by base type — used to inject is() overloads.
    private Dictionary<Type, List<(string Value, Type ConcreteType)>> _discriminatorMappingsByBase = [];

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
        // If this type has a user-registered alias / namespace mapping, emit
        // the resolved short name (optionally qualified with its mapped ns).
        if (typeDefinition.RawType is not null
            && _resolvedNames.TryGetValue(typeDefinition.RawType, out var resolved))
        {
            var baseName = !includeNamespace || string.IsNullOrEmpty(resolved.MappedNamespace)
                ? resolved.ShortName
                : $"{resolved.MappedNamespace}.{resolved.ShortName}";
            if (typeDefinition.GenericArguments.Count > 0)
                baseName += $"<{string.Join(", ", typeDefinition.GenericArguments.Select(s => BuildTypeString(s)))}>";
            if (typeDefinition.IsArray)
                baseName += "[]";
            if (typeDefinition.IsNullable)
                return $"({baseName} | null)";
            return baseName;
        }

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
        // Honor user aliases / namespace mappings for the human-readable
        // docstring-style type render too, so `@param` comments stay consistent
        // with the emitted declaration.
        if (typeDefinition.RawType is not null
            && _resolvedNames.TryGetValue(typeDefinition.RawType, out var resolved))
        {
            var baseName = !includeNamespace || string.IsNullOrEmpty(resolved.MappedNamespace)
                ? resolved.ShortName
                : $"{resolved.MappedNamespace}.{resolved.ShortName}";
            if (typeDefinition.GenericArguments.Count > 0)
                baseName += $"<{string.Join(", ", typeDefinition.GenericArguments.Select(s => GetTypeString(s)))}>";
            if (typeDefinition.IsArray)
                baseName += "[]";
            if (typeDefinition.IsNullable)
                return $"{baseName}?";
            return baseName;
        }

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
        // Header name for the declaration itself — if the type has a resolved
        // short name, emit that (unqualified, since we're inside the matching
        // namespace wrapper or at root scope).
        if (typeDefinition.RawType is not null
            && _resolvedNames.TryGetValue(typeDefinition.RawType, out var resolved))
        {
            var headerName = resolved.ShortName;
            if (typeDefinition.GenericArguments.Count > 0)
                headerName += $"<{string.Join(", ", typeDefinition.GenericArguments.Select(s => BuildTypeString(s)))}>";
            return headerName;
        }

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
        _rootScopeTypes.Clear();

        // Pre-compute the effective (namespace, short-name) for every type, given
        // the user's explicit aliases + namespace mappings. Collisions fail loudly
        // here — better to break the build than silently mis-route a type.
        //
        // Only types that were actually touched by a rule (Explicit / NamespaceMapping)
        // end up in _resolvedNames — Unchanged types keep going through the regular
        // BuildTypeString / NormalizeTypeName path, so built-in mappings like
        // `Task<T> → Promise<T>` still apply to them.
        var resolutions = TypeAliasResolver.Resolve(
            AllowedTypes,
            definitionBuilder.GetTypeAliases(),
            definitionBuilder.GetNamespaceMappings());
        _resolvedNames = resolutions
            .Where(r => r.Source != TypeAliasResolutionSource.Unchanged)
            .ToDictionary(
                r => r.Type,
                r => (r.MappedNamespace, r.ShortName));

        _discriminatorMappingsByBase = definitionBuilder.GetDiscriminatorMappings()
            .GroupBy(m => m.BaseType)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => (m.Value, m.ConcreteType)).ToList());

        foreach (var calculatedType in AllowedTypes)
        {
            var tdesc = TypeDefinition.FromType(calculatedType, AllowedTypes);
            var gn = GetNormalizedNonGenericTypeName(tdesc);
            if (gn is not null)
                tdesc.SetPayload<NormalizedNonGenericTypeName>(gn);

            if (tdesc.IsGeneric || tdesc.IsArray || tdesc.RawType == typeof(char))
                continue;

            var typeString = BuildTypeString(tdesc);
            if (typeString.EndsWith('&') || typeString.EndsWith('*'))
                continue;
            // Require namespace qualification unless the type was resolved to a
            // short name (alias or flattened mapping). Without this the root-scope
            // bucket below would never pick up anything — AllowedTypes includes
            // primitives/`any` whose BuildTypeString won't contain a dot.
            var rootScoped = tdesc.RawType is not null
                && _resolvedNames.TryGetValue(tdesc.RawType, out var res)
                && string.IsNullOrEmpty(res.MappedNamespace);
            if (!rootScoped && !typeString.Contains('.'))
                continue;

            if (rootScoped)
            {
                if (!_rootScopeTypes.Contains(tdesc))
                    _rootScopeTypes.Add(tdesc);
            }
            else
            {
                // Use the resolved (possibly remapped) namespace for placement.
                var targetNs = tdesc.RawType is not null && _resolvedNames.TryGetValue(tdesc.RawType, out var r)
                    ? r.MappedNamespace
                    : tdesc.Namespace;
                if (!string.IsNullOrWhiteSpace(targetNs))
                {
                    var ns = namespaceDefinition.AddNamespaceDefinition(targetNs);
                    // TypeDefinition.FromType is cached by FriendlyName, so distinct
                    // Type inputs that collapse to the same rendered identifier return
                    // the same TypeDefinition reference. Adding it twice would emit
                    // the declaration twice.
                    if (!ns.Types.Contains(tdesc))
                        ns.Types.Add(tdesc);
                }
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

        // Emit root-scope types (aliased or mapped to empty namespace) and/or the
        // `Type` discriminator const as a single ambient declaration file.
        if (_rootScopeTypes.Count > 0 || _discriminatorMappingsByBase.Count > 0)
            dict.Add("globals.d.ts", RenderRootScope());

        dict.Add("extensions.d.ts", RenderBuiltInExtensions());

        return dict;
    }

    private string RenderRootScope()
    {
        var strb = new StringBuilder();
        foreach (var tdesc in _rootScopeTypes.OrderBy(t =>
            t.RawType is not null && _resolvedNames.TryGetValue(t.RawType, out var r) ? r.ShortName : t.Name))
        {
            // Render at indent 0, prefix each declaration with `declare` to make
            // it an ambient module-scope declaration usable from any .ts file.
            var rendered = Render(tdesc, indent: 0);
            strb.AppendLine($"declare {rendered.TrimStart()}");
        }
        if (_discriminatorMappingsByBase.Count > 0)
            strb.AppendLine(RenderTypeConstDeclaration());
        return strb.ToString();
    }

    /// <summary>
    /// Emits <c>declare const Type: { Is(…); IsOneOf(…); }</c> plus a conditional
    /// type alias per base type so TypeScript can narrow the union correctly:
    /// <code>
    /// type AnimalByDiscriminator&lt;D extends 'dog' | 'cat'&gt; =
    ///     D extends 'dog' ? Dog :
    ///     D extends 'cat' ? Cat : never;
    ///
    /// declare const Type: {
    ///     Is(value: Animal, d: 'dog'): value is Dog;
    ///     IsOneOf&lt;D extends 'dog' | 'cat'&gt;(value: Animal, ds: readonly D[]): value is AnimalByDiscriminator&lt;D&gt;;
    ///     Is(value: object, d: string): boolean;
    ///     IsOneOf(value: object, ds: string[]): boolean;
    /// };
    /// </code>
    /// </summary>
    private string RenderTypeConstDeclaration()
    {
        var sb = new StringBuilder();

        // Conditional type alias per base type — needed for IsOneOf<D> return type.
        foreach (var (baseType, mappings) in _discriminatorMappingsByBase)
        {
            var baseDef = TypeDefinition.FromType(baseType, AllowedTypes);
            var baseTypeName = BuildTypeString(baseDef);
            var union = string.Join(" | ", mappings.Select(m => $"'{m.Value}'"));
            var mapTypeName = $"{baseType.Name}ByDiscriminator";

            sb.Append($"type {mapTypeName}<D extends {union}> =");
            foreach (var (value, concreteType) in mappings)
            {
                var concreteDef = TypeDefinition.FromType(concreteType, AllowedTypes);
                sb.AppendLine();
                sb.Append($"    D extends '{value}' ? {BuildTypeString(concreteDef)} :");
            }
            sb.AppendLine();
            sb.AppendLine("    never;");
            sb.AppendLine();
        }

        sb.AppendLine("declare const Type: {");
        foreach (var (baseType, mappings) in _discriminatorMappingsByBase)
        {
            var baseDef = TypeDefinition.FromType(baseType, AllowedTypes);
            var baseTypeName = BuildTypeString(baseDef);
            var union = string.Join(" | ", mappings.Select(m => $"'{m.Value}'"));
            var mapTypeName = $"{baseType.Name}ByDiscriminator";

            foreach (var (value, concreteType) in mappings)
            {
                var concreteDef = TypeDefinition.FromType(concreteType, AllowedTypes);
                sb.AppendLine($"    Is(value: {baseTypeName}, d: '{value}'): value is {BuildTypeString(concreteDef)};");
            }
            sb.AppendLine($"    IsOneOf<D extends {union}>(value: {baseTypeName}, ds: readonly D[]): value is {mapTypeName}<D>;");
        }
        sb.AppendLine("    Is(value: object, d: string): boolean;");
        sb.AppendLine("    IsOneOf(value: object, ds: string[]): boolean;");
        sb.Append("};");
        return sb.ToString();
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
