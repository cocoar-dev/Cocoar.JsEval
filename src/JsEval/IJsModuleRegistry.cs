using System.Collections.Generic;

namespace Cocoar.JsEval;

public interface IJsModuleRegistry
{
    IEnumerable<IJsModuleDefinition> GetRegisteredModuleDefinitions();
}
