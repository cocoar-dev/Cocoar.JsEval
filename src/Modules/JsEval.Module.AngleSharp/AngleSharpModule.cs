using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Cocoar.JsEval;

namespace Cocoar.JsEval.Module.AngleSharp;

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
