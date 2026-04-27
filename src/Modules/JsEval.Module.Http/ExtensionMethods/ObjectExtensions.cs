using System.Dynamic;
using System.Linq;
using System.Net.Http.Headers;
using Cocoar.JsEval;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.Http.ExtensionMethods;
#pragma warning restore CA1716

public static class ObjectExtensions
{
#pragma warning disable CA1720 // 'object' parameter contains type name — this is the `this` extension parameter; renaming is a breaking change
    public static ExpandoObject? ToExpandoObject(this object @object)
    {
        return JsonHelper.ToObject<ExpandoObject>(JsonHelper.ToJson(@object));
    }
#pragma warning restore CA1720

    public static ExpandoObject? ToExpandoObject(this HttpHeaders headers)
    {
        return headers.ToDictionary(h => h.Key, h => string.Join("; ", h.Value)).ToExpandoObject();
    }
}
