using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Cocoar.JsEval.Module.Http;

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

    private static HttpMessageHandler ValueFactory(HttpHandlerOptions handlerOptions)
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
            socketsHandler.SslOptions.RemoteCertificateValidationCallback +=
                (sender, certificate, chain, errors) => true;
        }

        socketsHandler.SslOptions.AllowRenegotiation = true;
        socketsHandler.SslOptions.EnabledSslProtocols = SslProtocols.None;
        return socketsHandler;
    }
}
