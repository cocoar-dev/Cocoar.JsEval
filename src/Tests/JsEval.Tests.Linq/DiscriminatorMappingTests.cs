using System.Linq.Expressions;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Cocoar.JsEval.TsDefinition;
using Xunit;
using static Cocoar.JsEval.Tests.Linq.TranslatorTestHelper;

namespace Cocoar.JsEval.Tests.Linq;

public class DiscriminatorMappingTests
{
    private static readonly TranslationOptions Opts = new()
    {
        DiscriminatorMappings =
        [
            new(typeof(Animal), "dog", typeof(Dog)),
            new(typeof(Animal), "cat", typeof(Cat)),
            new(typeof(Animal), "bird", typeof(Bird)),
        ]
    };

    // ── Translator tests ──────────────────────────────────────────────────────

    [Fact]
    public void Is_SingleType_ProducesTypeIs()
    {
        Expression<Func<Animal, bool>> expected = a => a is Dog;
        var actual = Translate<Animal, bool>("(a) => Type.Is(a, 'dog')", Opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void Is_Or_ProducesOrElse()
    {
        Expression<Func<Animal, bool>> expected = a => a is Dog || a is Cat;
        var actual = Translate<Animal, bool>("(a) => Type.Is(a, 'dog') || Type.Is(a, 'cat')", Opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void Is_AndNarrowing_AccessesSubtypeProperty()
    {
        Expression<Func<Animal, bool>> expected = a => a is Dog && ((Dog)a).BarkVolume > 50;
        var actual = Translate<Animal, bool>("(a) => Type.Is(a, 'dog') && a.BarkVolume > 50", Opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void Is_AndNarrowing_ChainedAnd_CarriesThrough()
    {
        Expression<Func<Animal, bool>> expected =
            a => a is Dog && ((Dog)a).BarkVolume > 50 && ((Dog)a).Breed == "Labrador";
        var actual = Translate<Animal, bool>(
            "(a) => Type.Is(a, 'dog') && a.BarkVolume > 50 && a.Breed === 'Labrador'", Opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void Is_OrAnd_SharedProperty_ResolvesViaIntersection()
    {
        // Dog and Cat both have BarkVolume — intersection narrowing picks Dog (first)
        var actual = Translate<Animal, bool>(
            "(a) => (Type.Is(a, 'dog') || Type.Is(a, 'cat')) && a.BarkVolume > 50", Opts);
        Assert.Contains("(a Is Dog) OrElse (a Is Cat)", actual.ToString());
        Assert.Contains("Convert(a, Dog).BarkVolume", actual.ToString());
    }

    [Fact]
    public void Is_BaseProperty_NeedsNoNarrowing()
    {
        Expression<Func<Animal, bool>> expected = a => a.Name.StartsWith("Rex");
        var actual = Translate<Animal, bool>("(a) => a.Name.startsWith('Rex')", Opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void Is_OrAnd_NonSharedProperty_Throws()
    {
        // Breed is on Dog only — Cat has no Breed → intersection fails
        Assert.Throws<InvalidOperationException>(() =>
            Translate<Animal, bool>("(a) => (Type.Is(a, 'dog') || Type.Is(a, 'cat')) && a.Breed === 'Labrador'", Opts));
    }

    [Fact]
    public void Is_NarrowingDoesNotLeakToOrRight()
    {
        // BarkVolume on the right of OR has no narrowing active
        Assert.Throws<InvalidOperationException>(() =>
            Translate<Animal, bool>("(a) => Type.Is(a, 'dog') || a.BarkVolume > 0", Opts));
    }

    [Fact]
    public void Is_WithoutMappings_FallsThroughToRegularCall()
    {
        // No discriminator mappings, no alias → Type.Is falls through to engine global lookup
        // With no engine, identifier 'Type' resolves as unresolved → throws
        Assert.Throws<InvalidOperationException>(() =>
            Translate<Animal, bool>("(a) => Type.Is(a, 'dog')", new TranslationOptions()));
    }

    [Fact]
    public void Is_TypeAlias_FallbackProducesTypeIs()
    {
        // No explicit DiscriminatorMapping for "Dog" — falls back to TypeAliases.
        var opts = new TranslationOptions
        {
            TypeAliases = new Dictionary<string, Type> { ["Dog"] = typeof(Dog) }
        };
        Expression<Func<Animal, bool>> expected = a => a is Dog;
        var actual = Translate<Animal, bool>("(a) => Type.Is(a, 'Dog')", opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void Is_ExplicitMappingWinsOverAlias()
    {
        // "dog" has an explicit mapping → uses that even if a TypeAlias also exists.
        var opts = new TranslationOptions
        {
            DiscriminatorMappings = [new(typeof(Animal), "dog", typeof(Dog))],
            TypeAliases = new Dictionary<string, Type> { ["dog"] = typeof(Cat) } // would be wrong if alias won
        };
        Expression<Func<Animal, bool>> expected = a => a is Dog;
        var actual = Translate<Animal, bool>("(a) => Type.Is(a, 'dog')", opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void Is_NamespaceMapping_RootFlatten_FallbackProducesTypeIs()
    {
        // Dog lives in Cocoar.JsEval.Tests.Linq — flattened to root → "Dog" resolves.
        var opts = new TranslationOptions
        {
            DiscriminatorMappings = [new(typeof(Animal), "irrelevant-key", typeof(Dog))],
            NamespaceMappings = [("Cocoar.JsEval.Tests.Linq", "")]
        };
        Expression<Func<Animal, bool>> expected = a => a is Dog;
        var actual = Translate<Animal, bool>("(a) => Type.Is(a, 'Dog')", opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void Is_NamespaceMapping_NonEmptyTarget_FallbackProducesTypeIs()
    {
        // Dog lives in Cocoar.JsEval.Tests.Linq — mapped to "Zoo" → "Zoo.Dog" resolves.
        var opts = new TranslationOptions
        {
            DiscriminatorMappings = [new(typeof(Animal), "irrelevant-key", typeof(Dog))],
            NamespaceMappings = [("Cocoar.JsEval.Tests.Linq", "Zoo")]
        };
        Expression<Func<Animal, bool>> expected = a => a is Dog;
        var actual = Translate<Animal, bool>("(a) => Type.Is(a, 'Zoo.Dog')", opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    // ── IsOneOf translator tests ──────────────────────────────────────────────

    [Fact]
    public void IsOneOf_SingleValue_ProducesTypeIs()
    {
        Expression<Func<Animal, bool>> expected = a => a is Dog;
        var actual = Translate<Animal, bool>("(a) => Type.IsOneOf(a, ['dog'])", Opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void IsOneOf_TwoValues_ProducesOrElse()
    {
        Expression<Func<Animal, bool>> expected = a => a is Dog || a is Cat;
        var actual = Translate<Animal, bool>("(a) => Type.IsOneOf(a, ['dog', 'cat'])", Opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void IsOneOf_AndNarrowing_AccessesSubtypeProperty()
    {
        // Same narrowing as explicit `Type.Is(a,'dog') && a.BarkVolume > 50`.
        Expression<Func<Animal, bool>> expected = a => (a is Dog || a is Cat) && ((Dog)a).BarkVolume > 50;
        var actual = Translate<Animal, bool>("(a) => Type.IsOneOf(a, ['dog','cat']) && a.BarkVolume > 50", Opts);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void IsOneOf_UnknownValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Translate<Animal, bool>("(a) => Type.IsOneOf(a, ['dog', 'unknown'])", Opts));
    }

    [Fact]
    public void IsOneOf_EmptyArray_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Translate<Animal, bool>("(a) => Type.IsOneOf(a, [])", Opts));
    }

    // ── TsDefinition tests ────────────────────────────────────────────────────

    [Fact]
    public void TsDefinition_EmitsDiscriminatorOverloads()
    {
        var result = new DefinitionBuilder()
            .AddType<Animal>()
            .AddType<Dog>()
            .AddType<Cat>()
            .AddDiscriminatorMappings<Animal>(
                ("dog", typeof(Dog)),
                ("cat", typeof(Cat)))
            .Render();

        Assert.True(result.ContainsKey("globals.d.ts"), "globals.d.ts must be emitted");
        var globalsFile = result["globals.d.ts"];
        Assert.Contains("declare const Type", globalsFile);
        Assert.Contains("d: 'dog'): value is", globalsFile);
        Assert.Contains("d: 'cat'): value is", globalsFile);
        Assert.Contains("Is(value: object, d: string): boolean;", globalsFile);
    }

    [Fact]
    public void TsDefinition_EmitsIsOneOfOverloads()
    {
        var result = new DefinitionBuilder()
            .AddType<Animal>()
            .AddType<Dog>()
            .AddType<Cat>()
            .AddDiscriminatorMappings<Animal>(
                ("dog", typeof(Dog)),
                ("cat", typeof(Cat)))
            .Render();

        var globalsFile = result["globals.d.ts"];
        // Conditional type alias
        Assert.Contains("type AnimalByDiscriminator<D extends 'dog' | 'cat'>", globalsFile);
        Assert.Contains("D extends 'dog' ?", globalsFile);
        Assert.Contains("D extends 'cat' ?", globalsFile);
        Assert.Contains("never;", globalsFile);
        // IsOneOf overload
        Assert.Contains("IsOneOf<D extends 'dog' | 'cat'>", globalsFile);
        Assert.Contains("ds: readonly D[]): value is AnimalByDiscriminator<D>", globalsFile);
        Assert.Contains("IsOneOf(value: object, ds: string[]): boolean;", globalsFile);
    }
}
