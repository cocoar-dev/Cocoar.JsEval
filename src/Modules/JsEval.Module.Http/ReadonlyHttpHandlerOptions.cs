using System;

namespace Cocoar.JsEval.Module.Http;

internal record ReadonlyHttpHandlerOptions(string DestinationHost, string? ProxyHost, bool IgnoreProxy, bool IgnoreCertificateErrors)
{
    public ReadonlyHttpHandlerOptions(HttpHandlerOptions httpHandlerOptions)
        : this(
            httpHandlerOptions.RequestUri.Host,
            httpHandlerOptions.Proxy?.Address?.Host,
            httpHandlerOptions.IgnoreProxy,
            httpHandlerOptions.IgnoreCertificateErrors)
    {
    }
}
