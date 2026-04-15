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

    private Dictionary<string, string>? _definitions;
    private Dictionary<string, string>? _imports;

    private readonly object _tsDefinitionsLock = new();
    private readonly object _tsImportsLock = new();

    public TsDefinitionService(IJsModuleRegistry moduleRegistry, JsEngineOptions? engineOptions = null)
    {
        _moduleRegistry = moduleRegistry;
        _engineOptions = engineOptions;
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
                defBuilder.AddExtensionMethods(_engineOptions.AllowedExtensionMethods);

            foreach (var md in definitions)
                defBuilder.AddTypes(md.ModuleType);

            _definitions = defBuilder.Render();
            var assembly = GetType().Assembly;

            _definitions["global.d.ts"] = assembly.ReadResourceAsString("global.d.ts")!;
            _definitions["lib.es2015.core.d.ts"] = assembly.ReadResourceAsString("lib.es2015.core.d.ts")!;
            _definitions["lib.es5.d.ts"] = assembly.ReadResourceAsString("lib.es5.d.ts")!;

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
