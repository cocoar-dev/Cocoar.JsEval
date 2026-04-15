using System;
using Cocoar.JsEval.Module.Http;
using Xunit;

namespace JsEval.Tests.Modules;

public class RetryHelperTests
{
    [Fact]
    public void ParseInput_SimpleEquality_ReturnsCorrectExpression()
    {
        var result = RetryHelper.ParseInput("500", "429");
        Assert.Equal("429==500", result);
    }

    [Fact]
    public void ParseInput_LessThan_ReturnsCorrectExpression()
    {
        var result = RetryHelper.ParseInput("<500", "200");
        Assert.Equal("200<500", result);
    }

    [Fact]
    public void ParseInput_Range_ReturnsCorrectExpression()
    {
        var result = RetryHelper.ParseInput("[400..500]", "429");
        Assert.Contains("429>=400", result);
        Assert.Contains("429<=500", result);
    }

    [Fact]
    public void ParseInput_Negated_ReturnsNegatedExpression()
    {
        var result = RetryHelper.ParseInput("not(200)", "500");
        Assert.StartsWith("!(", result);
        Assert.Contains("500==200", result);
    }

    [Fact]
    public void ParseInput_EmptyExpression_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => RetryHelper.ParseInput("", "200"));
    }

    [Fact]
    public void ParseInput_EmptyLeftSide_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => RetryHelper.ParseInput("500", ""));
    }

    [Fact]
    public void EvalExpression_SimpleTrue_ReturnsTrue()
    {
        var result = RetryHelper.EvalExpression("429==429");
        Assert.True(result);
    }

    [Fact]
    public void EvalExpression_SimpleFalse_ReturnsFalse()
    {
        var result = RetryHelper.EvalExpression("200==429");
        Assert.False(result);
    }

    [Fact]
    public void EvalExpression_RangeMatch_ReturnsTrue()
    {
        var result = RetryHelper.EvalExpression("(429>=400 && 429<=500)");
        Assert.True(result);
    }

    [Fact]
    public void EvalExpression_EmptyExpression_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => RetryHelper.EvalExpression(""));
    }

    [Fact]
    public void ParseInput_MultipleValues_JoinedWithOr()
    {
        var result = RetryHelper.ParseInput("429,500,503", "429");
        Assert.Contains("||", result);
        Assert.Contains("429==429", result);
        Assert.Contains("429==500", result);
        Assert.Contains("429==503", result);
    }
}
