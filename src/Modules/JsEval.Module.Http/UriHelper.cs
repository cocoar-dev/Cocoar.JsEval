using System;

namespace Cocoar.JsEval.Module.Http;

public static class UriHelper
{
    public static Uri BuildUri(string uri)
    {
        if (uri.StartsWith("//"))
            return new Uri("http:" + uri);
        if (uri.StartsWith("://"))
            return new Uri("http" + uri);

        var m = System.Text.RegularExpressions.Regex.Match(uri, @"^([^\/]+):(\d+)(\/*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        if (m.Success)
        {
            var port = int.Parse(m.Groups[2].Value);
            if (port <= 65535)
            {
                //part2 is a port (65535 highest port number)
                return new Uri("http://" + uri);
            }

            if (port >= 16777217)
            {
                //part2 is an ip long (16777217 first ip in long notation)
                return new UriBuilder(uri).Uri;
            }

            throw new ArgumentOutOfRangeException(nameof(uri), "Invalid port or ip long, technically could be local network hostname, but someone needs to be hit on the head for that one");
        }

        return new UriBuilder(uri).Uri;
    }
}
