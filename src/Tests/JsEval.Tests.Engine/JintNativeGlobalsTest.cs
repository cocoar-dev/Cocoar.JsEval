using Jint;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// Tests what Jint 4.8 provides natively (without JsEval wrapper).
/// This helps identify which globals are from Jint vs our custom additions.
/// </summary>
public class JintNativeGlobalsTest
{
    [Fact]
    public void ListAllNativeGlobals()
    {
        var engine = new Jint.Engine();
        string[] globals = ["console", "setTimeout", "setInterval", "clearTimeout", "clearInterval",
            "fetch", "structuredClone", "require", "atob", "btoa", "URL", "TextEncoder", "TextDecoder",
            "performance", "queueMicrotask", "alert", "prompt", "confirm"];

        foreach (var g in globals)
        {
            var type = engine.Evaluate($"typeof {g}").AsString();
            _output.WriteLine($"{g}: {type}");
        }
    }

    private readonly ITestOutputHelper _output;
    public JintNativeGlobalsTest(ITestOutputHelper output) => _output = output;
}
