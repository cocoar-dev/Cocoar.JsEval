using System;
using Cocoar.JsEval;

#pragma warning disable CA1716 // Module in namespace -- cannot rename without breaking change
namespace Cocoar.JsEval.Module.Http;
#pragma warning restore CA1716

public class HttpModule(IScriptEngine scriptEngine) : IJsModule
{
    private readonly IScriptEngine _scriptEngine = scriptEngine;

    public HttpRequestBuilder Client(string url, Action<HttpOptionsBuilder> options)
    {
        var builder = new HttpOptionsBuilder(url);
        options?.Invoke(builder);

        return new HttpRequestBuilder(builder, _scriptEngine);
    }

    public HttpRequestBuilder Client(string url, HttpHandlerOptions options)
    {
        return new HttpRequestBuilder(options, _scriptEngine);
    }

    public HttpRequestBuilder Client(string url)
    {
        var builder = new HttpOptionsBuilder(url);
        return new HttpRequestBuilder(builder, _scriptEngine);
    }
}
