using System;
using System.Collections.Generic;

namespace Cocoar.JsEval.TsDefinition;

internal static class Constants
{
    public static readonly string[] AccessModifiers =
    [
        "public",
        "protected internal",
        "protected",
        "internal",
        "private protected",
        "private"
    ];

    internal static List<Type> NumericTypes { get; } =
    [
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(float),
        typeof(ulong),
        typeof(double),
        typeof(decimal)
    ];
}
