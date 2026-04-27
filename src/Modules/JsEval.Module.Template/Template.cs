using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Cocoar.JsEval;
using Scriban.Runtime;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.Template;
#pragma warning restore CA1716

#pragma warning disable CA1822 // Instance methods required — Jint invokes these on a registered instance
public class TemplateModule : IJsModule
{
    public string Parse(string template, object data)
    {
        return Parse(template, new List<object> { data });
    }

    public string Parse(string template, params object[] data)
    {
        return Parse(template, data.ToList());
    }
#pragma warning restore CA1822

    private static string Parse(string template, IEnumerable<object> data)
    {
        var jsonObject = new JsonObject();
        jsonObject = data.Aggregate(jsonObject, (a, b) =>
        {
            var json = JsonHelper.ToJson(b);
            var jo = JsonHelper.ToJsonObject(json);
            return jo is not null ? JsonHelper.Merge(a, jo) : a;
        });

        var dict = JsonHelper.ToDictionary(jsonObject);

        return ParseScriptObject(template, dict!);
    }

    private static string ParseScriptObject(string template, Dictionary<string, object?> data)
    {
        var scriptObj = new ScriptObject(StringComparer.OrdinalIgnoreCase);
        scriptObj.Import(data, renamer: member => member.Name);
        var scribanTemplate = Scriban.Template.Parse(template);
        return scribanTemplate.Render(scriptObj);
    }
}
