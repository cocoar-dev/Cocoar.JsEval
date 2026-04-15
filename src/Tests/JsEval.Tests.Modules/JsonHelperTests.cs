using System.Text.Json.Nodes;
using Cocoar.JsEval;
using Xunit;

namespace JsEval.Tests.Modules;

public class JsonHelperTests
{
    [Fact]
    public void ToJson_SimpleObject_ReturnsValidJson()
    {
        var obj = new { Name = "Test", Value = 42 };
        var json = JsonHelper.ToJson(obj);

        Assert.Contains("Test", json);
        Assert.Contains("42", json);
    }

    [Fact]
    public void ToObject_ValidJson_ReturnsObject()
    {
        var json = """{"Name":"Test","Value":42}""";
        var result = JsonHelper.ToObject<TestDto>(json);

        Assert.NotNull(result);
        Assert.Equal("Test", result!.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Beautify_MinifiedJson_ReturnsFormatted()
    {
        var minified = """{"name":"test","value":1}""";
        var beautified = JsonHelper.Beautify(minified);

        Assert.Contains("\n", beautified);
    }

    [Fact]
    public void Flatten_NestedJsonObject_ReturnsFlatDictionary()
    {
        var jsonObj = JsonNode.Parse("""{"person":{"name":"test","age":30}}""")!.AsObject();
        var flat = JsonHelper.Flatten(jsonObj);

        Assert.True(flat.ContainsKey("person.name"));
        Assert.Equal("test", flat["person.name"]);
        Assert.True(flat.ContainsKey("person.age"));
    }

    [Fact]
    public void Unflatten_FlatDictionary_ReturnsNestedJsonObject()
    {
        var flat = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["person.name"] = "test",
            ["person.age"] = 30
        };
        var nested = JsonHelper.Unflatten(flat);

        Assert.NotNull(nested["person"]);
        var person = nested["person"]!.AsObject();
        Assert.Equal("test", person["name"]!.GetValue<string>());
    }

    [Fact]
    public void ToJson_Null_ReturnsNullString()
    {
        var json = JsonHelper.ToJson(null);
        Assert.Equal("null", json);
    }

    [Fact]
    public void ToJson_And_ToObject_RoundTrips()
    {
        var original = new TestDto { Name = "RoundTrip", Value = 99 };
        var json = JsonHelper.ToJson(original);
        var result = JsonHelper.ToObject<TestDto>(json);

        Assert.NotNull(result);
        Assert.Equal(original.Name, result!.Name);
        Assert.Equal(original.Value, result.Value);
    }

    private class TestDto
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }
}
