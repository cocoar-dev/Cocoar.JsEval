using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Cocoar.JsEval.Module.AngleSharp;

public static class AngleSharpModuleExtensions
{
    public static IHtmlDocument SetBaseHref(this IHtmlDocument htmlDocument, string href)
    {
        if (htmlDocument.Head is null)
            return htmlDocument;

        var baseHrefElement = htmlDocument.Head.GetElementsByTagName("base").FirstOrDefault();
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
        return htmlDocument.Head.GetElementsByTagName("meta").FirstOrDefault(m => m.GetAttribute("http-equiv") == "Content-Security-Policy")?.GetAttribute("content");
    }

    public static IHtmlDocument SetContentSecurityPolicy(this IHtmlDocument htmlDocument, string content)
    {
        if (htmlDocument.Head is null)
            return htmlDocument;

        htmlDocument.Head.GetElementsByTagName("meta").FirstOrDefault(m => m.GetAttribute("http-equiv") == "Content-Security-Policy")?.SetAttribute("content", content);

        return htmlDocument;
    }

    public static IHtmlDocument ReplaceContentSecurityPolicyParts(this IHtmlDocument htmlDocument, string from, string to)
    {
        if (htmlDocument.Head is null)
            return htmlDocument;
        var content = htmlDocument.Head.GetElementsByTagName("meta").FirstOrDefault(m => m.GetAttribute("http-equiv") == "Content-Security-Policy")?.GetAttribute("content") ?? "";
        content = content.Replace(from, to);
        htmlDocument.Head.GetElementsByTagName("meta").FirstOrDefault(m => m.GetAttribute("http-equiv") == "Content-Security-Policy")?.SetAttribute("content", content);
        return htmlDocument;
    }

    public static IElement[] ToArray(this IHtmlCollection<IElement> elements)
    {
        return [.. elements];
    }
}
