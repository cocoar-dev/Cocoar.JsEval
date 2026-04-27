using System;

namespace Cocoar.JsEval;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class JsModuleAttribute(params string[] tags) : Attribute
{
    public string? Name { get; set; }

    public string[] Tags { get; set; } = tags;
}
