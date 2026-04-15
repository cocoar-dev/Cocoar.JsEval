using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Cocoar.JsEval;
using Scriban.Runtime;

namespace Cocoar.JsEval.Module.Template;

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

    private string Parse(string template, IEnumerable<object> data)
    {
        var jsonObject = new JsonObject();
        data.Aggregate(jsonObject, (a, b) =>
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
