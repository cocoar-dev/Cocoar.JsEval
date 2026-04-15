using System;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// Tests for the browser-compatible fetch() global function.
/// Uses real HTTP calls to httpbin.org / jsonplaceholder.
/// </summary>
public class FetchTests
{
    private readonly ITestOutputHelper _output;

    public FetchTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private JsEngine CreateEngine()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b.EnableFetch());
        var sp = sc.BuildServiceProvider();
        return sp.GetRequiredService<JsEngine>();
    }

    [Fact]
    public async Task Fetch_SimpleGet_ReturnsOk()
    {
        using var jsEngine = CreateEngine();

        await jsEngine.ExecuteAsync(@"
const response = await fetch('https://jsonplaceholder.typicode.com/posts/1');
export const ok = response.ok;
export const status = response.status;
");

        Assert.True(jsEngine.GetValue<bool>("ok"));
        Assert.Equal(200, jsEngine.GetValue<int>("status"));
    }

    [Fact]
    public async Task Fetch_GetJson_ParsesResponse()
    {
        using var jsEngine = CreateEngine();

        await jsEngine.ExecuteAsync(@"
const response = await fetch('https://jsonplaceholder.typicode.com/posts/1');
const body = await response.text();
export const hasContent = body.length > 0;
export const statusText = response.statusText;
");

        Assert.True(jsEngine.GetValue<bool>("hasContent"));
    }

    [Fact]
    public async Task Fetch_PostWithBody_Works()
    {
        using var jsEngine = CreateEngine();

        await jsEngine.ExecuteAsync(@"
const response = await fetch('https://jsonplaceholder.typicode.com/posts', {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json'
    },
    body: JSON.stringify({
        title: 'Test Post',
        body: 'Hello from JsEval fetch!',
        userId: 1
    })
});
export const status = response.status;
export const ok = response.ok;
");

        var status = jsEngine.GetValue<int>("status");
        _output.WriteLine($"POST status: {status}");
        Assert.Equal(201, status);
        Assert.True(jsEngine.GetValue<bool>("ok"));
    }

    [Fact]
    public async Task Fetch_WithHeaders_SendsHeaders()
    {
        using var jsEngine = CreateEngine();

        await jsEngine.ExecuteAsync(@"
const response = await fetch('https://jsonplaceholder.typicode.com/posts/1', {
    headers: {
        'Accept': 'application/json',
        'X-Custom-Header': 'test-value'
    }
});
export const ok = response.ok;
");

        Assert.True(jsEngine.GetValue<bool>("ok"));
    }

    [Fact]
    public async Task Fetch_ResponseHeaders_Accessible()
    {
        using var jsEngine = CreateEngine();

        await jsEngine.ExecuteAsync(@"
const response = await fetch('https://jsonplaceholder.typicode.com/posts/1');
const contentType = response.headers['content-type'] || '';
export const hasJsonContentType = contentType.includes('json');
");

        Assert.True(jsEngine.GetValue<bool>("hasJsonContentType"));
    }

    [Fact]
    public async Task Fetch_404_ReturnsNotOk()
    {
        using var jsEngine = CreateEngine();

        await jsEngine.ExecuteAsync(@"
const response = await fetch('https://jsonplaceholder.typicode.com/posts/99999');
export const ok = response.ok;
export const status = response.status;
");

        Assert.False(jsEngine.GetValue<bool>("ok"));
        Assert.Equal(404, jsEngine.GetValue<int>("status"));
    }

    [Fact]
    public async Task Fetch_UrlProperty_IsSet()
    {
        using var jsEngine = CreateEngine();

        await jsEngine.ExecuteAsync(@"
const response = await fetch('https://jsonplaceholder.typicode.com/posts/1');
export const url = response.url;
");

        var url = jsEngine.GetValue<string>("url");
        Assert.Contains("jsonplaceholder", url);
    }

    [Fact]
    public async Task Fetch_NotAvailable_WithoutEnableFetch()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(); // no EnableFetch()
        var sp = sc.BuildServiceProvider();

        using var jsEngine = sp.GetRequiredService<JsEngine>();

        // fetch is not defined — script should throw
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await jsEngine.ExecuteAsync("export const r = await fetch('https://example.com');");
        });
    }
}
