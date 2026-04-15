using System;
using System.Collections.Generic;

namespace Cocoar.JsEval;

public record JsModuleDefinition(string Name, Type ModuleType) : IJsModuleDefinition
{
    public IReadOnlyList<string>? Tags { get; set; } = [];
}
