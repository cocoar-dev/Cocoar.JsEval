using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace Cocoar.JsEval.Module.Http;

public class HttpHandlerOptions(Uri uri)
{
    public Uri RequestUri { get; } = uri;
    public WebProxy? Proxy { get; set; }
    public bool IgnoreProxy { get; set; }
    public bool IgnoreCertificateErrors { get; set; } = true;

    public List<X509Certificate2> ClientCertificates { get; set; } = [];

    public HttpCompletionOption HttpCompletionOption { get; set; } = HttpCompletionOption.ResponseContentRead;
}
