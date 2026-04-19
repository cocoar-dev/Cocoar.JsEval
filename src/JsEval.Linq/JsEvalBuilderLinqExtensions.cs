using Cocoar.JsEval.Engine;

namespace Cocoar.JsEval.Linq;

/// <summary>
/// Builder integration — one call enables JS-to-Expression translation for
/// every <see cref="IQueryable{T}"/> exposed to the engine.
/// </summary>
public static class JsEvalBuilderLinqExtensions
{
    /// <summary>
    /// Registers <see cref="JsLinqExtensions"/> as Jint extension methods. After this,
    /// <c>someQueryable.where(u =&gt; ...)</c> from JS produces a real Expression tree
    /// and routes through the underlying LINQ provider.
    /// </summary>
    public static JsEvalBuilder AddLinq(this JsEvalBuilder builder)
    {
        builder.AddExtensionMethods(typeof(JsLinqExtensions));
        builder.RegisterEngineConfigurator(LinqCasts.Register);
        // Contributes linq.d.ts to TsDefinitionService.GetTsDefinitions() — so
        // Monaco / tsc see the `linq.*` global automatically without the host
        // having to hand-wire it.
        builder.AddTsDefinitionContributor<LinqTsContributor>();
        return builder;
    }
}
