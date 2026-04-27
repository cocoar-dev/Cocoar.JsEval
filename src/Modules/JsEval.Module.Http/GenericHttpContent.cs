using System;
using System.IO;
using System.Net.Http;
using Cocoar.JsEval;
using Nito.AsyncEx.Synchronous;

#pragma warning disable CA1716 // Module in namespace -- cannot rename without breaking change
namespace Cocoar.JsEval.Module.Http;
#pragma warning restore CA1716

public class GenericHttpContent : IDisposable
{
    private readonly HttpContent _httpContent;
    private readonly IScriptEngine _scriptEngine;

    public GenericHttpContent(HttpContent httpContent, IScriptEngine scriptEngine)
    {
        _httpContent = httpContent;
        _scriptEngine = scriptEngine;
    }

    public string AsText()
    {
        return _httpContent.ReadAsStringAsync().WaitAndUnwrapException();
    }

    [Obsolete("Use 'AsObjectFromJson' instead")]
    public object? AsObject()
    {
        return AsObjectFromJson();
    }

    public Stream AsStream()
    {
        return _httpContent.ReadAsStream();
    }

    public object? AsObjectFromJson()
    {
        return _scriptEngine.JsonParse(AsText());
    }

    public void Dispose()
    {
        _httpContent?.Dispose();
        GC.SuppressFinalize(this);
    }
}
