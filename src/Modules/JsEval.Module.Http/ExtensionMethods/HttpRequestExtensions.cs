using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Cocoar.JsEval.Module.Http.ExtensionMethods;

internal static class HttpContentExtensions
{
    internal static Dictionary<string, string> GetContentHeaders(this HttpResponseMessage requestMessage) =>
        requestMessage.Content.Headers.ToDictionary(kvp => kvp.Key, kvp => string.Join(", ", kvp.Value));

    internal static Dictionary<string, string> GetHeaders(this HttpResponseMessage requestMessage) =>
        requestMessage.Headers.ToDictionary(kvp => kvp.Key, kvp => string.Join(", ", kvp.Value));
}
