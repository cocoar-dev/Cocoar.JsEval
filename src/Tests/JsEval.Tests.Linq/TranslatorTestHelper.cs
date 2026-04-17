using System.Linq.Expressions;
using Cocoar.JsEval.Linq;
using Jint;

namespace Cocoar.JsEval.Tests.Linq;

internal static class TranslatorTestHelper
{
    public static Expression<Func<T, TResult>> Translate<T, TResult>(string jsArrowFunction, Action<Jint.Engine>? configureEngine = null)
    {
        var engine = new Jint.Engine();
        configureEngine?.Invoke(engine);
        var jsFn = engine.Evaluate(jsArrowFunction);
        return JsExpressionTranslator.Translate<T, TResult>(jsFn, engine);
    }

    /// <summary>Renders an Expression tree using its built-in ToString(), which is deterministic and readable.</summary>
    public static string Render<T, TResult>(Expression<Func<T, TResult>> expr) => expr.ToString();
}
