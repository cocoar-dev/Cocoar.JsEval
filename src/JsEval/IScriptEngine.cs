namespace Cocoar.JsEval;

/// <summary>
/// Minimal interface for script engine capabilities needed by modules.
/// Implemented by JavaScriptEngine.
/// </summary>
public interface IScriptEngine
{
    object? JsonParse(string? json);
    string JsonStringify(object? value);
}
