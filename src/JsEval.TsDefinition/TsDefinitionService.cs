using System.Collections.Generic;
using System.Linq;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.TsDefinition.Definitions;

namespace Cocoar.JsEval.TsDefinition;

public class TsDefinitionService
{
    private readonly IJsModuleRegistry _moduleRegistry;
    private readonly JsEngineOptions? _engineOptions;
    private readonly IEnumerable<IJsTsDefinitionContributor> _contributors;

    private Dictionary<string, string>? _definitions;
    private Dictionary<string, string>? _imports;

    private readonly object _tsDefinitionsLock = new();
    private readonly object _tsImportsLock = new();

    public TsDefinitionService(IJsModuleRegistry moduleRegistry, JsEngineOptions? engineOptions = null)
        : this(moduleRegistry, engineOptions, contributors: null)
    {
    }

    public TsDefinitionService(
        IJsModuleRegistry moduleRegistry,
        JsEngineOptions? engineOptions,
        IEnumerable<IJsTsDefinitionContributor>? contributors)
    {
        _moduleRegistry = moduleRegistry;
        _engineOptions = engineOptions;
        _contributors = contributors ?? System.Array.Empty<IJsTsDefinitionContributor>();
    }

    public Dictionary<string, string> GetTsDefinitions()
    {
        lock (_tsDefinitionsLock)
        {
            if (_definitions is not null)
                return new Dictionary<string, string>(_definitions);

            var definitions = _moduleRegistry.GetRegisteredModuleDefinitions().ToList();
            var defBuilder = new DefinitionBuilder();

            if (_engineOptions is not null)
            {
                defBuilder.AddExtensionMethods(_engineOptions.AllowedExtensionMethods);

                // Mirror user-configured aliases + namespace mappings from the
                // engine side into the builder so both .d.ts emission AND
                // NewObject(...) resolution use the same short names.
                foreach (var kv in _engineOptions.TypeAliases)
                    defBuilder.AddType(kv.Value, kv.Key);
                foreach (var mapping in _engineOptions.NamespaceMappings)
                    defBuilder.MapNamespace(mapping.Source, mapping.Target);

                // Mirror CLR-type discriminator mappings → `declare const Type { Is(...): value is Concrete }`.
                // Property-based mappings (PropertyName != null, ConcreteType == null) have no CLR subtype
                // to narrow to, so they fall back to the generic boolean overload — skip them here.
                foreach (var grp in _engineOptions.DiscriminatorMappings
                    .Where(m => m.ConcreteType != null)
                    .GroupBy(m => m.BaseType))
                    defBuilder.AddDiscriminatorMappings(
                        grp.Key,
                        grp.Select(m => (m.Value, m.ConcreteType!)).ToArray());
            }

            foreach (var md in definitions)
                defBuilder.AddTypes(md.ModuleType);

            _definitions = defBuilder.Render();
            var assembly = GetType().Assembly;

            _definitions["global.d.ts"] = assembly.ReadResourceAsString("global.d.ts")!;

            // Contributor output is applied last — hosts that want to override the
            // package's bundled files (or any earlier contributor's) can do so by
            // registering their IJsTsDefinitionContributor behind the default ones.
            foreach (var contributor in _contributors)
                foreach (var kv in contributor.GetTsDefinitions())
                    _definitions[kv.Key] = kv.Value;

            return new Dictionary<string, string>(_definitions);
        }
    }

    public Dictionary<string, string> GetTsImports()
    {
        lock (_tsImportsLock)
        {
            if (_imports is not null)
                return new Dictionary<string, string>(_imports);

            var definitions = _moduleRegistry.GetRegisteredModuleDefinitions().ToList();

            _imports = definitions.ToDictionary(
                md => $"{md.Name}.ts",
                md =>
                {
                    var tsr = new TypeScriptRenderer();
                    var td = TypeDefinition.FromType(md.ModuleType);

                    return tsr.RenderBody(td, 0, (definition, s) => definition switch
                    {
                        MethodDefinition => $"export function {s}",
                        PropertyDefinition => $"export const {s}",
                        _ => s
                    });
                });

            return new Dictionary<string, string>(_imports);
        }
    }
}
