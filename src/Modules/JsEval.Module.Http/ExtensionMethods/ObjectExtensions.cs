using System.Dynamic;
using System.Linq;
using System.Net.Http.Headers;
using Cocoar.JsEval;

namespace Cocoar.JsEval.Module.Http.ExtensionMethods;

public static class ObjectExtensions
{
    public static ExpandoObject? ToExpandoObject(this object @object)
    {
        return JsonHelper.ToObject<ExpandoObject>(JsonHelper.ToJson(@object));
    }

    public static ExpandoObject? ToExpandoObject(this HttpHeaders headers)
    {
        return headers.ToDictionary(h => h.Key, h => string.Join("; ", h.Value)).ToExpandoObject();
    }
}
