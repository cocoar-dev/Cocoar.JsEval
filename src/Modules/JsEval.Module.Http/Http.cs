using System;
using Cocoar.JsEval;

namespace Cocoar.JsEval.Module.Http;

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
