using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.Http;
#pragma warning restore CA1716

public static class HttpHandlerFactory
{
    private static readonly ConcurrentDictionary<ReadonlyHttpHandlerOptions, HttpMessageHandler> HttpHandlers = new();
    private static readonly ConcurrentDictionary<ReadonlyHttpHandlerOptions, HttpClient> HttpClients = new();

    public static HttpMessageHandler Build(HttpHandlerOptions handlerOptions)
    {
        var roHttpHandlerOptions = new ReadonlyHttpHandlerOptions(handlerOptions);
        return HttpHandlers.GetOrAdd(roHttpHandlerOptions, _ => ValueFactory(handlerOptions));
    }

    public static HttpClient GetClient(HttpHandlerOptions handlerOptions)
    {
        var roHttpHandlerOptions = new ReadonlyHttpHandlerOptions(handlerOptions);
        return HttpClients.GetOrAdd(roHttpHandlerOptions, _ => new HttpClient(Build(handlerOptions)));
    }

    private static SocketsHttpHandler ValueFactory(HttpHandlerOptions handlerOptions)
    {
        var socketsHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromSeconds(60),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(20),
            MaxConnectionsPerServer = 2,
            AutomaticDecompression = DecompressionMethods.All,
        };

        if (handlerOptions.Proxy is not null)
        {
            socketsHandler.Proxy = handlerOptions.Proxy;
        }

        if (handlerOptions.IgnoreProxy)
        {
            socketsHandler.UseProxy = false;
        }

        if (handlerOptions.ClientCertificates is not null)
        {
            foreach (var handlerOptionsClientCertificate in handlerOptions.ClientCertificates)
            {
                socketsHandler.SslOptions.ClientCertificates ??= new X509CertificateCollection();
                socketsHandler.SslOptions.ClientCertificates.Add(handlerOptionsClientCertificate);
            }
        }

        if (handlerOptions.IgnoreCertificateErrors)
        {
#pragma warning disable CA5359 // Intentional: user-opted-in certificate bypass for dev/internal scenarios
            socketsHandler.SslOptions.RemoteCertificateValidationCallback +=
                (sender, certificate, chain, errors) => true;
#pragma warning restore CA5359
        }

        socketsHandler.SslOptions.AllowRenegotiation = true;
        socketsHandler.SslOptions.EnabledSslProtocols = SslProtocols.None;
        return socketsHandler; // SocketsHttpHandler (concrete) rather than HttpMessageHandler (interface) — CA1859
    }
}
