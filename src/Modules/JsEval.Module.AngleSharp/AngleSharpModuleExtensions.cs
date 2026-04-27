using System;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.AngleSharp;
#pragma warning restore CA1716

public static class AngleSharpModuleExtensions
{
    public static IHtmlDocument SetBaseHref(this IHtmlDocument htmlDocument, string href)
    {
        if (htmlDocument.Head is null)
            return htmlDocument;

#pragma warning disable CA1826 // FirstOrDefault is used intentionally for null-safety over direct index access
        var baseHrefElement = htmlDocument.Head.GetElementsByTagName("base").FirstOrDefault();
#pragma warning restore CA1826
        if (baseHrefElement is not null)
        {
            baseHrefElement.SetAttribute("href", href);
        }
        else
        {
            var bElement = htmlDocument.CreateElement("base");
            bElement.SetAttribute("href", href);

            var firstHeadChildElement = htmlDocument.Head.FirstElementChild;
            if (firstHeadChildElement is null)
            {
                htmlDocument.Head.AppendElement(bElement);
            }
            else
            {
                htmlDocument.Head.InsertBefore(bElement, firstHeadChildElement);
            }
        }

        return htmlDocument;
    }

    public static string? GetContentSecurityPolicy(this IHtmlDocument htmlDocument)
    {
        if (htmlDocument.Head is null)
            return null;
        return htmlDocument.Head.GetElementsByTagName("meta").FirstOrDefault(m => string.Equals(m.GetAttribute("http-equiv"), "Content-Security-Policy", StringComparison.Ordinal))?.GetAttribute("content");
    }

    public static IHtmlDocument SetContentSecurityPolicy(this IHtmlDocument htmlDocument, string content)
    {
        if (htmlDocument.Head is null)
            return htmlDocument;

        htmlDocument.Head.GetElementsByTagName("meta").FirstOrDefault(m => string.Equals(m.GetAttribute("http-equiv"), "Content-Security-Policy", StringComparison.Ordinal))?.SetAttribute("content", content);

        return htmlDocument;
    }

    public static IHtmlDocument ReplaceContentSecurityPolicyParts(this IHtmlDocument htmlDocument, string from, string to)
    {
        if (htmlDocument.Head is null)
            return htmlDocument;
        var content = htmlDocument.Head.GetElementsByTagName("meta").FirstOrDefault(m => string.Equals(m.GetAttribute("http-equiv"), "Content-Security-Policy", StringComparison.Ordinal))?.GetAttribute("content") ?? "";
        content = content.Replace(from, to, StringComparison.Ordinal);
        htmlDocument.Head.GetElementsByTagName("meta").FirstOrDefault(m => string.Equals(m.GetAttribute("http-equiv"), "Content-Security-Policy", StringComparison.Ordinal))?.SetAttribute("content", content);
        return htmlDocument;
    }

    public static IElement[] ToArray(this IHtmlCollection<IElement> elements)
    {
        return [.. elements];
    }
}
