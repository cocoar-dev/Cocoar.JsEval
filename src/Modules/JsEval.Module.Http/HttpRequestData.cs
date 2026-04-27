using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Cocoar.JsEval;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

#pragma warning disable CA1716 // Module in namespace -- cannot rename without breaking change
namespace Cocoar.JsEval.Module.Http;
#pragma warning restore CA1716

public class HttpRequestData
{
    public ConcurrentDictionary<string, StringValues> QueryParameters { get; } = new();
    public ConcurrentDictionary<string, List<string>> Headers { get; } = new();

    public string? ContentType { get; set; }

    public List<string> PathSegments { get; set; } = [];

    public HttpRequestMessage BuildHttpRequestMessage(HttpHandlerOptions httpHandlerOptions, HttpMethod httpMethod, object? content)
    {
        var requestUri = PathSegments.Count == 0
            ? httpHandlerOptions.RequestUri
            : new Uri(httpHandlerOptions.RequestUri, string.Join('/', PathSegments));

        if (!QueryParameters.IsEmpty)
        {
            var qb = new QueryBuilder();
            if (!string.IsNullOrWhiteSpace(requestUri.Query))
            {
                var query = QueryHelpers.ParseQuery(requestUri.Query);
                foreach (var kv in query)
                {
                    qb.Add(kv.Key, kv.Value.OfType<string>());
                }
            }
            foreach (var kv in QueryParameters)
            {
                qb.Add(kv.Key, kv.Value.OfType<string>());
            }

            var uriBuilder = new UriBuilder(requestUri)
            {
                Query = qb.ToQueryString().ToString()
            };

            requestUri = uriBuilder.Uri;
        }

        var message = new HttpRequestMessage(httpMethod, requestUri);

        if (content is not null)
        {
            message.Content = CreateHttpContent(content);
        }

        if (!Headers.IsEmpty)
        {
            message.Headers.Clear();
            foreach (var kv in Headers)
            {
                if (kv.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;

                message.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(ContentType) && message.Content is not null)
        {
            message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType);
        }

        return message;
    }

    private static HttpContent? CreateHttpContent(object? content)
    {
        if (content is null)
            return null;

        if (content is Stream stream)
        {
            return new StreamContent(stream);
        }

        HttpContent httpContent;

        if (content is string str)
        {
            httpContent = new StringContent(str, Encoding.UTF8);
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return httpContent;
        }

        var ms = new MemoryStream();
        CreateJsonHttpContent(content, ms);
        ms.Seek(0, SeekOrigin.Begin);
        httpContent = new StreamContent(ms);
        httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return httpContent;
    }

    private static void CreateJsonHttpContent(object content, Stream stream)
    {
        JsonHelper.Serialize(stream, content);
    }
}
