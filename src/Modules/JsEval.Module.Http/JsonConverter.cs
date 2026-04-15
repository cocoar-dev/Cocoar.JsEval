using System.Text.Json;
using Cocoar.JsEval;

namespace Cocoar.JsEval.Module.Http;

public static class Converter
{
    public static JsonSerializerOptions Options => JsonHelper.Options;
}
