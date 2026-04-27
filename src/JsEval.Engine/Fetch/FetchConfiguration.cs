using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;

namespace Cocoar.JsEval.Engine.Fetch;

/// <summary>
/// Optional fetch configuration object, registered as global `fetchOptions`.
/// In browser environments this object doesn't exist, so scripts should use
/// optional chaining: fetchOptions?.ignoreCertificateErrors(true)
/// </summary>
internal sealed class FetchConfiguration : IDisposable
{
    private bool _ignoreCertificateErrors;
    private int _timeoutSeconds = 30;
    private string? _proxyUrl;
    private bool _configured;
    private bool _dirty = true;
    private HttpClient? _client;
    private readonly object _lock = new();

    internal bool IsDefault => !_configured;

    public FetchConfiguration ignoreCertificateErrors(bool value)
    {
        _ignoreCertificateErrors = value;
        _configured = true;
        _dirty = true;
        return this;
    }

    public FetchConfiguration timeout(int seconds)
    {
        _timeoutSeconds = seconds;
        _configured = true;
        _dirty = true;
        return this;
    }

    public FetchConfiguration proxy(string url)
    {
        _proxyUrl = url;
        _configured = true;
        _dirty = true;
        return this;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        GC.SuppressFinalize(this);
    }

    internal HttpClient GetClient()
    {
        if (!_dirty && _client is not null)
            return _client;

        lock (_lock)
        {
            if (!_dirty && _client is not null)
                return _client;

            _client?.Dispose();

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
            };

            if (_ignoreCertificateErrors)
            {
#pragma warning disable CA5359 // Intentional: user-opted-in certificate bypass for dev/internal scenarios
                handler.SslOptions.RemoteCertificateValidationCallback =
                    (_, _, _, _) => true;
#pragma warning restore CA5359
            }

            handler.SslOptions.EnabledSslProtocols = SslProtocols.None;

            if (_proxyUrl is not null)
            {
                handler.Proxy = new WebProxy(_proxyUrl);
            }

            _client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_timeoutSeconds)
            };

            _dirty = false;
            return _client;
        }
    }
}
