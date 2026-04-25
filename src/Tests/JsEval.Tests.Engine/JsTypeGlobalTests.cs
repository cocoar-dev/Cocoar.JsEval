using System;
using System.Collections.Generic;
using Cocoar.JsEval.Engine;
using Xunit;

namespace JsEval.Tests.Engine;

public class JsTypeGlobalTests
{
    // Nested so they live in namespace "JsEval.Tests.Engine" with short names "Shape"/"Circle"/"Square".
    private abstract class Shape { }
    private sealed class Circle : Shape { public double Radius { get; set; } }
    private sealed class Square : Shape { public double Side { get; set; } }

    private static readonly DiscriminatorMapping[] Mappings =
    [
        new(typeof(Shape), "circle", typeof(Circle)),
        new(typeof(Shape), "square", typeof(Square)),
    ];

    [Fact]
    public void Is_ExplicitMapping_ReturnsTrue()
    {
        var sut = new JsTypeGlobal(Mappings, new Dictionary<string, Type>(), []);
        Assert.True(sut.Is(new Circle(), "circle"));
        Assert.False(sut.Is(new Square(), "circle"));
    }

    [Fact]
    public void Is_TypeAlias_Fallback_ReturnsTrue()
    {
        var aliases = new Dictionary<string, Type> { ["Circ"] = typeof(Circle) };
        var sut = new JsTypeGlobal([], aliases, []);
        Assert.True(sut.Is(new Circle(), "Circ"));
        Assert.False(sut.Is(new Square(), "Circ"));
    }

    [Fact]
    public void Is_NamespaceMapping_RootFlatten_ReturnsTrue()
    {
        // Circle is in JsEval.Tests.Engine — flattened to root → "Circle" resolves.
        var sut = new JsTypeGlobal(Mappings, new Dictionary<string, Type>(),
            [("JsEval.Tests.Engine", "")]);
        Assert.True(sut.Is(new Circle(), "Circle"));
        Assert.False(sut.Is(new Square(), "Circle"));
    }

    [Fact]
    public void Is_NamespaceMapping_NonEmptyTarget_ReturnsTrue()
    {
        // Circle is in JsEval.Tests.Engine — mapped to "Geo" → "Geo.Circle" resolves.
        var sut = new JsTypeGlobal(Mappings, new Dictionary<string, Type>(),
            [("JsEval.Tests.Engine", "Geo")]);
        Assert.True(sut.Is(new Circle(), "Geo.Circle"));
        Assert.False(sut.Is(new Square(), "Geo.Circle"));
    }

    [Fact]
    public void Is_ExplicitMappingWinsOverNamespaceMapping()
    {
        var sut = new JsTypeGlobal(Mappings, new Dictionary<string, Type>(),
            [("JsEval.Tests.Engine", "")]);
        Assert.True(sut.Is(new Circle(), "circle"));   // explicit key
        Assert.True(sut.Is(new Circle(), "Circle"));   // namespace-mapped key, both work
    }

    [Fact]
    public void Is_Null_ReturnsFalse()
    {
        var sut = new JsTypeGlobal(Mappings, new Dictionary<string, Type>(), []);
        Assert.False(sut.Is(null!, "circle"));
    }
}
