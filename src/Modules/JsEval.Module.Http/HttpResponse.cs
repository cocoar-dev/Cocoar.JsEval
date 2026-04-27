using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net;
using System.Net.Http;
using Cocoar.Reflectensions;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.JsEval.Module.Http.ExtensionMethods;
using Cocoar.JsEval;

#pragma warning disable CA1716 // Module in namespace -- cannot rename without breaking change
namespace Cocoar.JsEval.Module.Http;
#pragma warning restore CA1716

public class HttpResponse : IDisposable
{
    private readonly HttpResponseMessage _httpResponseMessage;
    private readonly IScriptEngine _scriptEngine;

    public Version Version => _httpResponseMessage.Version;

    private GenericHttpContent? _content;
    public GenericHttpContent Content => _content ??= new GenericHttpContent(_httpResponseMessage.Content, _scriptEngine);

    public HttpStatusCode StatusCode => _httpResponseMessage.StatusCode;
    public string? ReasonPhrase => _httpResponseMessage.ReasonPhrase;

    private Dictionary<string, string>? _headers;

    public Dictionary<string, string> Headers => _headers ??=
        _httpResponseMessage.GetHeaders().Merge(_httpResponseMessage.GetContentHeaders());

    private ExpandableObject? _trailingheaders;
    public ExpandableObject? TrailingHeaders => _trailingheaders ??= new ExpandableObject(_httpResponseMessage.TrailingHeaders);

    public bool IsSuccessStatusCode => _httpResponseMessage.IsSuccessStatusCode;

    public HttpResponse EnsureSuccessStatusCode()
    {
        _httpResponseMessage.EnsureSuccessStatusCode();
        return this;
    }

    public override string ToString() => _httpResponseMessage.ToString();

    internal HttpResponse(HttpResponseMessage httpResponseMessage, IScriptEngine scriptEngine)
    {
        _httpResponseMessage = httpResponseMessage;
        _scriptEngine = scriptEngine;
    }

    public void Dispose()
    {
        _httpResponseMessage?.Dispose();
        GC.SuppressFinalize(this);
    }
}
