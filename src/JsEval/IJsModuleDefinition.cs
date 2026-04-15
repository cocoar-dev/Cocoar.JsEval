using System;
using System.Collections.Generic;

namespace Cocoar.JsEval;

public interface IJsModuleDefinition
{
    string Name { get; }
    IReadOnlyList<string>? Tags { get; }
    Type ModuleType { get; }
}
