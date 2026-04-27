using System;

#pragma warning disable CA1716 // Module in namespace -- cannot rename without breaking change
namespace Cocoar.JsEval.Module.Http;
#pragma warning restore CA1716

internal sealed record ReadonlyHttpHandlerOptions(string DestinationHost, string? ProxyHost, bool IgnoreProxy, bool IgnoreCertificateErrors)
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
