using System.Text.Json;
using Cocoar.JsEval;

#pragma warning disable CA1716 // Module in namespace -- cannot rename without breaking change
namespace Cocoar.JsEval.Module.Http;
#pragma warning restore CA1716

public static class Converter
{
    public static JsonSerializerOptions Options => JsonHelper.Options;
}
