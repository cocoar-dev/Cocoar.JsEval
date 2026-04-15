using System;

namespace Cocoar.JsEval;

public class JsModuleAttribute(params string[] tags) : Attribute
{
    public string? Name { get; set; }

    public string[] Tags { get; set; } = tags;
}
