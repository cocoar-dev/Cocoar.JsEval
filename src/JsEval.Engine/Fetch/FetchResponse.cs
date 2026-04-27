using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Cocoar.JsEval.Engine.Fetch;

/// <summary>
/// Browser-compatible Response object returned by fetch().
/// Matches the Web API: https://developer.mozilla.org/en-US/docs/Web/API/Response
/// </summary>
internal sealed class FetchResponse : IDisposable
{
    private readonly HttpResponseMessage _response;
    private string? _cachedBody;
    private bool _disposed;

    internal FetchResponse(HttpResponseMessage response, string url)
    {
        _response = response;
        this.url = url;
        status = (int)response.StatusCode;
        statusText = response.ReasonPhrase ?? "";
        ok = response.IsSuccessStatusCode;
        redirected = response.RequestMessage?.RequestUri?.ToString() != url;
        type = "basic";

        var headerDict = new Dictionary<string, string>();
        foreach (var header in response.Headers)
            headerDict[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
        foreach (var header in response.Content.Headers)
            headerDict[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
        headers = headerDict;
    }

    public bool ok { get; }
    public int status { get; }
    public string statusText { get; }
    public string url { get; }
    public bool redirected { get; }
    public string type { get; }
    public Dictionary<string, string> headers { get; }

    public async Task<string> text()
    {
        _cachedBody ??= await _response.Content.ReadAsStringAsync();
        return _cachedBody;
    }

    public async Task<string> json()
    {
        return await text();
    }

    public async Task<byte[]> arrayBuffer()
    {
        return await _response.Content.ReadAsByteArrayAsync();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _response.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
