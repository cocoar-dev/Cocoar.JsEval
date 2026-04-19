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
        // IQueryable<T> pipeline: .where/.count/.any/.find/.orderBy take JS lambdas
        // and translate them to real Expression trees via JsExpressionTranslator.
        builder.AddExtensionMethods(typeof(JsLinqExtensions));

        // PascalCase aliases on JS-native strings — real BCL wrappers so Jint
        // resolves `"abc".Contains('x')` / `.StartsWith(…)` / `.ToLower()` at
        // plain runtime, not just inside translator-processed predicate lambdas.
        // Runtime-only: the TS surface comes from IJsStringAliases via
        // LinqTsContributor, so registering these for .d.ts too would duplicate
        // the `interface String` augmentation across extensions.d.ts and
        // cocoar-jseval-linq.d.ts.
        builder.AddRuntimeOnlyExtensionMethods(typeof(JsStringRuntimeAliases));

        // PascalCase aliases on JS-native arrays — System.Linq.Enumerable covers
        // .Where/.Any/.All/.Select/.FirstOrDefault/.Contains etc. on any
        // IEnumerable<T>, which JS arrays marshal to. Jint's extension resolution
        // routes `[1,2,3].Where(x => …)` to Enumerable.Where<T>.
        //
        // Registered as *runtime-only* so the 200+ Enumerable signatures don't
        // bloat TsDefinition's emitted System.d.ts — the TS surface is described
        // narrowly by IJsArrayAliases<T> which LinqTsContributor projects as
        // `interface Array<T> { … }` / `ReadonlyArray<T>` augmentations.
        builder.AddRuntimeOnlyExtensionMethods(typeof(System.Linq.Enumerable));

        // Binds `linq` global (linq.guid('…'), linq.decimal('…'), …).
        builder.RegisterEngineConfigurator(LinqCasts.Register);

        // Contributes linq.d.ts + cocoar-jseval-linq.d.ts to TsDefinitionService —
        // both are reflection-generated from LinqGlobal / IJsStringAliases /
        // IJsArrayAliases<T> by LinqTsContributor.
        builder.AddTsDefinitionContributor<LinqTsContributor>();
        return builder;
    }
}
