using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Cocoar.JsEval.Linq;

/// <summary>
/// Contributes the <c>.d.ts</c> files owned by <c>Cocoar.JsEval.Linq</c> to
/// <c>TsDefinitionService.GetTsDefinitions()</c> — all reflection-generated
/// from C# types in this assembly so the runtime and Monaco view can't drift.
/// <list type="bullet">
///   <item><c>linq.d.ts</c> — generated from <see cref="LinqGlobal"/>'s public instance methods</item>
///   <item><c>cocoar-jseval-linq.d.ts</c> — generated from <see cref="IJsStringAliases"/> and <see cref="IJsArrayAliases{T}"/> metadata interfaces</item>
/// </list>
/// Registered in DI by <c>JsEvalBuilder.AddLinq()</c>.
/// </summary>
internal sealed class LinqTsContributor : IJsTsDefinitionContributor
{
    private static readonly string _linqDts = BuildLinqDts();
    private static readonly string _aliasesDts = BuildAliasesDts();

    public IEnumerable<KeyValuePair<string, string>> GetTsDefinitions()
    {
        yield return new KeyValuePair<string, string>("linq.d.ts", _linqDts);
        yield return new KeyValuePair<string, string>("cocoar-jseval-linq.d.ts", _aliasesDts);
    }

    // ---------------- linq.d.ts ----------------

    private static string BuildLinqDts()
    {
        var sb = new StringBuilder();
        AppendHeader(sb,
            "Cocoar.JsEval.Linq — `linq` runtime global (typed-literal helpers)",
            "Generated from the LinqGlobal C# class via reflection.",
            "Change LinqGlobal's methods, not this file.");
        sb.AppendLine();
        sb.AppendLine("/// <reference path=\"./System.d.ts\" />");
        sb.AppendLine();
        sb.AppendLine("declare const linq: {");
        foreach (var m in DeclaredInstanceMethods(typeof(LinqGlobal)))
            sb.AppendLine($"    {m.Name}({RenderParameters(m)}): {MapType(m.ReturnType)};");
        sb.AppendLine("};");
        return sb.ToString();
    }

    // ---------------- cocoar-jseval-linq.d.ts ----------------

    private static string BuildAliasesDts()
    {
        var sb = new StringBuilder();
        AppendHeader(sb,
            "Cocoar.JsEval.Linq — C# LINQ method aliases on String / Array<T>",
            "Generated from IJsStringAliases and IJsArrayAliases<T> metadata shapes.",
            "Drop this into your scripts workspace (or let TsDefinitionService surface it",
            "automatically) so Monaco recognizes `u.Name.Contains('x')` / `arr.Any(…)` etc.",
            "At runtime JsExpressionTranslator's method-map + reflection-fallback resolves",
            "these PascalCase calls to the real BCL / LINQ methods on the emitted",
            "Expression tree.");
        sb.AppendLine();

        EmitInterfaceAugmentation(sb, "String", typeof(IJsStringAliases), genericParam: null);
        EmitInterfaceAugmentation(sb, "Array<T>", typeof(IJsArrayAliases<>), genericParam: "T");
        // Mirror onto ReadonlyArray<T> so literal array annotations (readonly T[])
        // also pick up the aliases.
        EmitInterfaceAugmentation(sb, "ReadonlyArray<T>", typeof(IJsArrayAliases<>), genericParam: "T");

        return sb.ToString();
    }

    private static void EmitInterfaceAugmentation(StringBuilder sb, string targetInterface, Type shape, string? genericParam)
    {
        sb.AppendLine($"interface {targetInterface} {{");
        foreach (var m in DeclaredInstanceMethods(shape))
        {
            var ret = MapType(m.ReturnType, genericParam);
            if (IsNullable(m.ReturnParameter))
                ret = $"{ret} | undefined";
            sb.AppendLine($"    {m.Name}{RenderMethodGenerics(m)}({RenderParameters(m)}): {ret};");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    // ---------------- helpers ----------------

    private static IEnumerable<MethodInfo> DeclaredInstanceMethods(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
         .OrderBy(m => m.MetadataToken);

    private static string RenderMethodGenerics(MethodInfo m)
    {
        var args = m.GetGenericArguments();
        return args.Length == 0 ? "" : $"<{string.Join(", ", args.Select(a => a.Name))}>";
    }

    private static string RenderParameters(MethodInfo m)
    {
        return string.Join(", ", m.GetParameters().Select(p =>
        {
            // Mark a parameter optional only if it has a default value in the
            // C# declaration. Nullability-metadata alone isn't enough — a
            // non-default `T value` param on a generic interface gets
            // reported as Nullable by NullabilityInfoContext depending on
            // context, which would falsely mark `Contains(value: T)` as
            // `Contains(value?: T)`.
            var optional = p.HasDefaultValue ? "?" : "";
            return $"{p.Name}{optional}: {MapType(p.ParameterType)}";
        }));
    }

    // True when the type was declared with an explicit `?` nullable annotation
    // (works for both reference types and generic parameters via
    // NullabilityInfoContext — used on return values where hand-written d.ts
    // needed "T | undefined" for `T? FirstOrDefault(...)`).
    private static bool IsNullable(ParameterInfo p) =>
        new NullabilityInfoContext().Create(p).ReadState == NullabilityState.Nullable;

    // Matches TsDefinition's TypeScriptRendererDefaults so references from
    // projections and linq returns land on the same identifier.
    private static string MapType(Type t, string? genericParam = null)
    {
        var nonNull = Nullable.GetUnderlyingType(t) ?? t;

        // Method-level or interface-level generic parameter (T, R, …) — emit by name.
        if (nonNull.IsGenericParameter)
            return genericParam == "T" && nonNull.Name == "T" ? "T" : nonNull.Name;

        if (nonNull == typeof(string) || nonNull == typeof(char)) return "string";
        if (nonNull == typeof(bool)) return "boolean";
        if (nonNull == typeof(void)) return "void";
        if (nonNull == typeof(object)) return "any";
        if (nonNull == typeof(Guid)) return "System.Guid";
        if (nonNull == typeof(DateTime) || nonNull == typeof(DateTimeOffset)
            || nonNull == typeof(DateOnly) || nonNull == typeof(TimeOnly))
            return "Date";
        if (nonNull == typeof(decimal) || nonNull == typeof(double) || nonNull == typeof(float)
            || nonNull == typeof(int) || nonNull == typeof(long) || nonNull == typeof(short)
            || nonNull == typeof(byte) || nonNull == typeof(sbyte)
            || nonNull == typeof(uint) || nonNull == typeof(ulong) || nonNull == typeof(ushort)
            || nonNull == typeof(TimeSpan))
            return "number";

        if (nonNull.IsArray)
            return $"{MapType(nonNull.GetElementType()!, genericParam)}[]";

        // Func<T, R> → (arg0: T) => R — mirrors TypeCache.BuildFuncTypeName.
        if (nonNull.IsGenericType)
        {
            var def = nonNull.GetGenericTypeDefinition();
            var args = nonNull.GetGenericArguments();
            if (def == typeof(Func<>) || def == typeof(Func<,>) || def == typeof(Func<,,>) || def == typeof(Func<,,,>))
            {
                var ret = MapType(args[^1], genericParam);
                var pars = string.Join(", ", args.Take(args.Length - 1)
                    .Select((a, i) => $"arg{i}: {MapType(a, genericParam)}"));
                return $"(({pars}) => {ret})";
            }
        }

        return nonNull.FullName ?? nonNull.Name;
    }

    private static void AppendHeader(StringBuilder sb, params string[] lines)
    {
        sb.AppendLine("// ============================================================================");
        foreach (var line in lines)
            sb.AppendLine($"// {line}");
        sb.AppendLine("// ============================================================================");
    }
}
