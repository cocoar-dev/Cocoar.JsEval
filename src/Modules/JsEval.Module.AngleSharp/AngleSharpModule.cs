using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Cocoar.JsEval;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.AngleSharp;
#pragma warning restore CA1716

#pragma warning disable CA1822 // Instance methods required — Jint invokes these on a registered instance
public class AngleSharpModule : IJsModule
{
    public IHtmlDocument ParseHtml(string html)
    {
        var parser = new HtmlParser();
        return parser.ParseDocument(html);
    }

    public IHtmlDocument ParseHtmlFromFile(string filePath)
    {
        var html = File.ReadAllText(filePath);
        return ParseHtml(html);
    }
}
#pragma warning restore CA1822
