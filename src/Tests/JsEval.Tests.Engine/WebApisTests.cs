using System;
using Cocoar.JsEval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// Tests for Web API globals: atob/btoa, performance.now(), TextEncoder/TextDecoder.
/// </summary>
public class WebApisTests
{
    private readonly ITestOutputHelper _output;

    public WebApisTests(ITestOutputHelper output) => _output = output;

    private JsEngine CreateEngine()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        return sc.BuildServiceProvider().GetRequiredService<JsEngine>();
    }

    // --- btoa / atob ---

    [Fact]
    public void Btoa_EncodesStringToBase64()
    {
        using var engine = CreateEngine();
        engine.Evaluate("var encoded = btoa('Hello, World!');");
        Assert.Equal("SGVsbG8sIFdvcmxkIQ==", engine.GetValue<string>("encoded"));
    }

    [Fact]
    public void Atob_DecodesBase64ToString()
    {
        using var engine = CreateEngine();
        engine.Evaluate("var decoded = atob('SGVsbG8sIFdvcmxkIQ==');");
        Assert.Equal("Hello, World!", engine.GetValue<string>("decoded"));
    }

    [Fact]
    public void BtoaAtob_RoundTrip()
    {
        using var engine = CreateEngine();
        engine.Evaluate("var original = 'Test äöü 123'; var result = atob(btoa(original));");
        Assert.Equal("Test äöü 123", engine.GetValue<string>("result"));
    }

    [Fact]
    public void Btoa_EmptyString()
    {
        using var engine = CreateEngine();
        engine.Evaluate("var encoded = btoa('');");
        Assert.Equal("", engine.GetValue<string>("encoded"));
    }

    // --- performance.now() ---

    [Fact]
    public void PerformanceNow_ReturnsNumber()
    {
        using var engine = CreateEngine();
        engine.Evaluate("var t = performance.now();");
        var t = engine.GetValue<double>("t");
        Assert.True(t >= 0);
        _output.WriteLine($"performance.now() = {t:F3} ms");
    }

    [Fact]
    public void PerformanceNow_Increases()
    {
        using var engine = CreateEngine();
        engine.Evaluate(@"
            var t1 = performance.now();
            // Do some work
            var sum = 0;
            for (var i = 0; i < 10000; i++) sum += i;
            var t2 = performance.now();
            var elapsed = t2 - t1;
        ");
        var elapsed = engine.GetValue<double>("elapsed");
        Assert.True(elapsed >= 0);
        _output.WriteLine($"Elapsed: {elapsed:F3} ms");
    }

    // --- TextEncoder ---

    [Fact]
    public void TextEncoder_EncodeString()
    {
        using var engine = CreateEngine();
        engine.Evaluate(@"
            var encoder = new TextEncoder();
            var bytes = encoder.encode('ABC');
            var len = bytes.length;
            var first = bytes[0];
        ");
        Assert.Equal(3, engine.GetValue<int>("len"));
        Assert.Equal(65, engine.GetValue<int>("first")); // 'A' = 65
    }

    [Fact]
    public void TextEncoder_EncodeEmptyString()
    {
        using var engine = CreateEngine();
        engine.Evaluate(@"
            var encoder = new TextEncoder();
            var bytes = encoder.encode('');
            var len = bytes.length;
        ");
        Assert.Equal(0, engine.GetValue<int>("len"));
    }

    [Fact]
    public void TextEncoder_EncodeUnicode()
    {
        using var engine = CreateEngine();
        engine.Evaluate(@"
            var encoder = new TextEncoder();
            var bytes = encoder.encode('ä');
            var len = bytes.length;
        ");
        // 'ä' is 2 bytes in UTF-8 (0xC3 0xA4)
        Assert.Equal(2, engine.GetValue<int>("len"));
    }

    // --- TextDecoder ---

    [Fact]
    public void TextDecoder_DecodeBytes()
    {
        using var engine = CreateEngine();
        engine.Evaluate(@"
            var encoder = new TextEncoder();
            var decoder = new TextDecoder();
            var bytes = encoder.encode('Hello');
            var text = decoder.decode(bytes);
        ");
        Assert.Equal("Hello", engine.GetValue<string>("text"));
    }

    [Fact]
    public void TextEncoder_TextDecoder_RoundTrip()
    {
        using var engine = CreateEngine();
        engine.Evaluate(@"
            var original = 'Grüße aus Österreich! 🎉';
            var encoder = new TextEncoder();
            var decoder = new TextDecoder();
            var result = decoder.decode(encoder.encode(original));
        ");
        Assert.Equal("Grüße aus Österreich! 🎉", engine.GetValue<string>("result"));
    }

    [Fact]
    public void TextDecoder_HasEncodingProperty()
    {
        using var engine = CreateEngine();
        engine.Evaluate("var enc = new TextDecoder('utf-8').encoding;");
        Assert.Equal("utf-8", engine.GetValue<string>("enc"));
    }

    // --- All available in typeof check ---

    [Theory]
    [InlineData("atob", "function")]
    [InlineData("btoa", "function")]
    [InlineData("performance", "object")]
    [InlineData("TextEncoder", "function")]
    [InlineData("TextDecoder", "function")]
    public void Global_IsAvailable(string name, string expectedType)
    {
        using var engine = CreateEngine();
        engine.Evaluate($"var t = typeof {name};");
        Assert.Equal(expectedType, engine.GetValue<string>("t"));
    }
}
