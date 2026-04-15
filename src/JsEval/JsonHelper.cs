using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cocoar.JsEval;

public static class JsonHelper
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions BeautifyOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static JsonSerializerOptions Options => SerializerOptions;

    public static string ToJson(object? value)
    {
        if (value is null)
            return "null";

        return JsonSerializer.Serialize(value, SerializerOptions);
    }

    public static string ToJson(object? value, bool beautify)
    {
        if (value is null)
            return "null";

        return JsonSerializer.Serialize(value, beautify ? BeautifyOptions : SerializerOptions);
    }

    public static T? ToObject<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    public static object? ToObject(string? json, Type type)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize(json, type, SerializerOptions);
    }

    public static JsonObject? ToJsonObject(string json)
    {
        return JsonNode.Parse(json)?.AsObject();
    }

    public static JsonNode? ToJsonNode(string json)
    {
        return JsonNode.Parse(json);
    }

    public static string Beautify(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        var node = JsonNode.Parse(json);
        return node?.ToJsonString(BeautifyOptions) ?? json;
    }

    public static string Minify(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        var node = JsonNode.Parse(json);
        return node?.ToJsonString(SerializerOptions) ?? json;
    }

    public static JsonObject Merge(JsonObject target, JsonObject source)
    {
        foreach (var property in source)
        {
            if (property.Value is null)
            {
                target[property.Key] = null;
            }
            else if (property.Value is JsonObject sourceObj && target[property.Key] is JsonObject targetObj)
            {
                Merge(targetObj, sourceObj);
            }
            else
            {
                target[property.Key] = property.Value.DeepClone();
            }
        }
        return target;
    }

    public static Dictionary<string, object?>? ToDictionary(JsonObject? jsonObject)
    {
        if (jsonObject is null)
            return null;

        var result = new Dictionary<string, object?>();
        foreach (var property in jsonObject)
        {
            result[property.Key] = ConvertJsonNode(property.Value);
        }
        return result;
    }

    private static object? ConvertJsonNode(JsonNode? node)
    {
        if (node is null)
            return null;

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var str))
                return str;
            if (jsonValue.TryGetValue<long>(out var lng))
                return lng;
            if (jsonValue.TryGetValue<double>(out var dbl))
                return dbl;
            if (jsonValue.TryGetValue<bool>(out var bl))
                return bl;
            return jsonValue.ToString();
        }

        if (node is JsonObject jsonObject)
        {
            return ToDictionary(jsonObject);
        }

        if (node is JsonArray jsonArray)
        {
            return jsonArray.Select(ConvertJsonNode).ToList();
        }

        return node.ToString();
    }

    public static Dictionary<string, object?> Flatten(JsonObject jsonObject, string separator = ".")
    {
        var result = new Dictionary<string, object?>();
        FlattenInternal(jsonObject, "", separator, result);
        return result;
    }

    private static void FlattenInternal(JsonNode? node, string prefix, string separator, Dictionary<string, object?> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    var newPrefix = string.IsNullOrEmpty(prefix)
                        ? property.Key
                        : $"{prefix}{separator}{property.Key}";
                    FlattenInternal(property.Value, newPrefix, separator, result);
                }
                break;

            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    var newPrefix = $"{prefix}[{i}]";
                    FlattenInternal(array[i], newPrefix, separator, result);
                }
                break;

            case JsonValue value:
                result[prefix] = ConvertJsonNode(value);
                break;

            case null:
                result[prefix] = null;
                break;
        }
    }

    public static JsonObject Unflatten(Dictionary<string, object?> flatDict, string separator = ".")
    {
        var result = new JsonObject();

        foreach (var kvp in flatDict)
        {
            var keys = ParsePath(kvp.Key, separator);
            SetNestedValue(result, keys, 0, CreateJsonNode(kvp.Value));
        }

        return result;
    }

    private static JsonNode? CreateJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            string s => JsonValue.Create(s),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            float f => JsonValue.Create(f),
            decimal dec => JsonValue.Create(dec),
            bool b => JsonValue.Create(b),
            DateTime dt => JsonValue.Create(dt),
            _ => JsonNode.Parse(JsonSerializer.Serialize(value, SerializerOptions))
        };
    }

    private static List<string> ParsePath(string path, string separator)
    {
        var result = new List<string>();
        var current = new StringBuilder();

        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == '[')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                var endBracket = path.IndexOf(']', i);
                if (endBracket > i)
                {
                    result.Add(path.Substring(i + 1, endBracket - i - 1));
                    i = endBracket;
                }
            }
            else if (separator.Length == 1 && path[i] == separator[0])
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else if (separator.Length > 1 && path.AsSpan(i).StartsWith(separator))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                i += separator.Length - 1;
            }
            else
            {
                current.Append(path[i]);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static void SetNestedValue(JsonNode node, List<string> keys, JsonNode? value)
    {
        SetNestedValue(node, keys, 0, value);
    }

    private static void SetNestedValue(JsonNode node, List<string> keys, int keyIndex, JsonNode? value)
    {
        if (keyIndex >= keys.Count)
            return;

        var currentKey = keys[keyIndex];
        var isLast = keyIndex == keys.Count - 1;

        if (isLast)
        {
            if (node is JsonObject obj)
            {
                obj[currentKey] = value;
            }
            else if (node is JsonArray arr && int.TryParse(currentKey, out var index))
            {
                while (arr.Count <= index)
                    arr.Add(null);
                arr[index] = value;
            }
            return;
        }

        var nextKey = keys[keyIndex + 1];
        var isNextArray = int.TryParse(nextKey, out _);

        if (node is JsonObject jObj)
        {
            if (jObj[currentKey] is null)
            {
                jObj[currentKey] = isNextArray ? new JsonArray() : new JsonObject();
            }
            SetNestedValue(jObj[currentKey]!, keys, keyIndex + 1, value);
        }
        else if (node is JsonArray jArr && int.TryParse(currentKey, out var index))
        {
            while (jArr.Count <= index)
                jArr.Add(null);

            if (jArr[index] is null)
            {
                jArr[index] = isNextArray ? new JsonArray() : new JsonObject();
            }
            SetNestedValue(jArr[index]!, keys, keyIndex + 1, value);
        }
    }

    public static void Serialize(Stream stream, object? value)
    {
        JsonSerializer.Serialize(stream, value, SerializerOptions);
    }

    public static void Serialize(Utf8JsonWriter writer, object? value)
    {
        JsonSerializer.Serialize(writer, value, SerializerOptions);
    }
}
