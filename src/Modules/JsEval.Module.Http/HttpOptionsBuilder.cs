using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable CA1716 // Module in namespace -- cannot rename without breaking change
namespace Cocoar.JsEval.Module.Http;
#pragma warning restore CA1716

public class HttpOptionsBuilder
{
    private readonly HttpHandlerOptions _httpHandlerOptions;

    public HttpOptionsBuilder(string url)
    {
        _httpHandlerOptions = new HttpHandlerOptions(UriHelper.BuildUri(url));
    }

    public HttpOptionsBuilder UseProxy(WebProxy proxy)
    {
        _httpHandlerOptions.IgnoreProxy = false;
        _httpHandlerOptions.Proxy = proxy;
        return this;
    }

    public HttpOptionsBuilder UseProxy(System.Uri proxy)
    {
        return UseProxy(new WebProxy(proxy));
    }

    public HttpOptionsBuilder UseProxy(System.Uri proxy, ICredentials credentials)
    {
        var webProxy = new WebProxy(proxy)
        {
            Credentials = credentials
        };
        return UseProxy(webProxy);
    }

    public HttpOptionsBuilder UseProxy(string proxy)
    {
        var uri = UriHelper.BuildUri(proxy);
        return UseProxy(uri);
    }

    public HttpOptionsBuilder UseProxy(string proxy, ICredentials credentials)
    {
        var uri = UriHelper.BuildUri(proxy);
        return UseProxy(uri, credentials);
    }

    public HttpOptionsBuilder IgnoreProxy()
    {
        return IgnoreProxy(true);
    }

    public HttpOptionsBuilder IgnoreProxy(bool value)
    {
        _httpHandlerOptions.IgnoreProxy = value;
        return this;
    }

    public HttpOptionsBuilder AddClientCertificate(byte[] bytes)
    {
        var cert = X509CertificateLoader.LoadCertificate(bytes);
        _httpHandlerOptions.ClientCertificates.Add(cert);
        return this;
    }

    public HttpOptionsBuilder AddClientCertificate(byte[] bytes, string password)
    {
        var cert = X509CertificateLoader.LoadPkcs12(bytes, password);
        _httpHandlerOptions.ClientCertificates.Add(cert);
        return this;
    }

    public HttpOptionsBuilder SetHttpCompletionOption(HttpCompletionOption completionOption)
    {
        this._httpHandlerOptions.HttpCompletionOption = completionOption;
        return this;
    }

    public static implicit operator HttpHandlerOptions(HttpOptionsBuilder optionsOptionsBuilder)
    {
        return optionsOptionsBuilder._httpHandlerOptions;
    }
}
