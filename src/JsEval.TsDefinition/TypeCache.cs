using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cocoar.JsEval.TsDefinition.Definitions;

namespace Cocoar.JsEval.TsDefinition;

internal static class TypeCache
{
    public static ConcurrentDictionary<string, TypeDefinition> Cache { get; } = new();

    public static TypeDefinition JsAny { get; } = new() { Name = "any" };
    public static TypeDefinition JsNone { get; } = new() { Name = "none" };
    public static TypeDefinition JsString { get; } = new() { Name = "String" };

    public static string BuildActionTypeName(Type type, List<string> args)
    {
        var parameters = args.Select((arg, i) => $"arg{i}: {arg}");
        return $"(({string.Join(", ", parameters)}) => void)";
    }

    public static string BuildFuncTypeName(Type type, List<string> args)
    {
        var parameters = args.Take(args.Count - 1).Select((arg, i) => $"arg{i}: {arg}");
        return $"(({string.Join(", ", parameters)}) => {args.Last()})";
    }

    public static string BuildPredicateTypeName(Type type, List<string> args)
    {
        var parameters = args.Select((arg, i) => $"arg{i}: {arg}");
        return $"(({string.Join(", ", parameters)}) => boolean)";
    }
}
