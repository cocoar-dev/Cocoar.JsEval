using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using Jint.Native.Object;

namespace Cocoar.JsEval.Engine.Fetch;

/// <summary>
/// Implements the browser-compatible fetch() global function.
/// Usage in scripts: const response = await fetch(url, options?)
/// </summary>
internal class FetchHandler
{
    private static readonly HttpClient DefaultClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly FetchConfiguration _config;

    private FetchHandler(FetchConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Register fetch() and fetchOptions as globals on the Jint engine.
    /// </summary>
    public static void Register(Jint.Engine engine)
    {
        var config = new FetchConfiguration();
        var handler = new FetchHandler(config);

        engine.SetValue("fetch", new Func<string, JsValue, Task<FetchResponse>>(
            (url, options) => handler.ExecuteFetch(engine, url, options)));
        engine.SetValue("fetchOptions", config);
    }

    private async Task<FetchResponse> ExecuteFetch(Jint.Engine engine, string url, JsValue options)
    {
        using var request = BuildRequest(url, options);
        var client = _config.IsDefault ? DefaultClient : _config.GetClient();
        var response = await client.SendAsync(request);
        return new FetchResponse(response, url);
    }

    private static HttpRequestMessage BuildRequest(string url, JsValue options)
    {
        var method = HttpMethod.Get;
        string? body = null;
        ObjectInstance? headers = null;

        if (options is ObjectInstance opts)
        {
            var methodValue = opts.Get("method");
            if (!methodValue.IsUndefined() && !methodValue.IsNull())
                method = HttpMethod.Parse(methodValue.AsString());

            var bodyValue = opts.Get("body");
            if (!bodyValue.IsUndefined() && !bodyValue.IsNull())
                body = bodyValue.AsString();

            var headersValue = opts.Get("headers");
            if (headersValue is ObjectInstance headersObj)
                headers = headersObj;
        }

        var request = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            var contentType = "text/plain";

            if (headers is not null)
            {
                var ct = headers.Get("Content-Type");
                if (!ct.IsUndefined() && !ct.IsNull())
                    contentType = ct.AsString();
                ct = headers.Get("content-type");
                if (!ct.IsUndefined() && !ct.IsNull())
                    contentType = ct.AsString();
            }

            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        if (headers is not null)
        {
            foreach (var pair in headers.GetOwnProperties())
            {
                var key = pair.Key.AsString();
                var value = pair.Value.Value.AsString();

                if (key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                    continue;

                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        return request;
    }
}
