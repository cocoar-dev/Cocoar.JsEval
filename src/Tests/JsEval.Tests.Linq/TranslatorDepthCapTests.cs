using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.JsEval.Tests.Linq;

/// <summary>
/// Pins the lib-side depth cap that prevents StackOverflowException on
/// deeply-nested AST. Reproduces F6 from .local/security-untrusted-script-hardening.md
/// — without the cap, these scripts would crash the host process unrecoverably.
/// </summary>
public class TranslatorDepthCapTests
{
    private static JsEngine BuildEngine()
    {
        var services = new ServiceCollection();
        services.AddJsEval();
        return services.BuildServiceProvider().GetRequiredService<JsEngine>();
    }

    [Fact]
    public void Translate_DeeplyNestedTernary_ThrowsInvalidOperationException_NotStackOverflow()
    {
        var engine = BuildEngine();

        // 500 nested ternaries: comfortably below Acornima's parser stack limit,
        // but well above the translator's default MaxAstDepth (256). Without
        // the depth cap, the recursive Visit chain would StackOverflow here —
        // killing the test process rather than failing this assertion.
        var body = "true";
        for (int i = 0; i < 500; i++) body = $"({body} ? 1 : 2)";
        var jsFn = engine.EvaluateExpression($"(p) => {body} === 1");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            JsExpressionTranslator.Translate<object, bool>(jsFn, engine));
        Assert.Contains("MaxAstDepth", ex.Message);
    }

    [Fact]
    public void Translate_RespectsCustomMaxAstDepth()
    {
        var engine = BuildEngine();

        // Depth 12 ternary chain, cap set to 10 → must throw.
        var body = "true";
        for (int i = 0; i < 12; i++) body = $"({body} ? 1 : 2)";
        var jsFn = engine.EvaluateExpression($"(p) => {body} === 1");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            JsExpressionTranslator.Translate<object, bool>(
                jsFn, engine, new TranslationOptions { MaxAstDepth = 10 }));
        Assert.Contains("10", ex.Message);
    }

    [Fact]
    public void Translate_ShallowPredicate_PassesUnderDefaultCap()
    {
        var engine = BuildEngine();
        var jsFn = engine.EvaluateExpression("(u) => u.Name === 'Alice' && u.Age > 18");

        // Default cap (256) is comfortably above any hand-written predicate.
        var lambda = JsExpressionTranslator.Translate<TestUser, bool>(jsFn, engine);

        var compiled = lambda.Compile();
        Assert.True(compiled(new TestUser { Name = "Alice", Age = 30 }));
        Assert.False(compiled(new TestUser { Name = "Bob", Age = 30 }));
    }
}
