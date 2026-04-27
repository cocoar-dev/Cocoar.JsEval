using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.Http.ExtensionMethods;
#pragma warning restore CA1716

internal static class HttpContentExtensions
{
    internal static Dictionary<string, string> GetContentHeaders(this HttpResponseMessage requestMessage) =>
        requestMessage.Content.Headers.ToDictionary(kvp => kvp.Key, kvp => string.Join(", ", kvp.Value));

    internal static Dictionary<string, string> GetHeaders(this HttpResponseMessage requestMessage) =>
        requestMessage.Headers.ToDictionary(kvp => kvp.Key, kvp => string.Join(", ", kvp.Value));
}
