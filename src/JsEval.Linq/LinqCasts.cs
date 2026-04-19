using System.Globalization;
using Jint;

namespace Cocoar.JsEval.Linq;

/// <summary>
/// Registers a <c>linq</c> global object on a Jint engine with strongly-typed
/// constructor helpers that produce real .NET primitives from string input.
///
/// <para>
/// JavaScript has only one numeric type (<c>number</c>, IEEE-754 double) which
/// means literal precision is lost before .NET ever sees it. These helpers
/// solve that — but only in one specific context:
/// </para>
///
/// <para>
/// <b>Precision is guaranteed at translation time, not runtime.</b>
/// When <see cref="JsExpressionTranslator"/> encounters <c>linq.decimal('99.99')</c>
/// inline in a predicate, it emits a typed <c>Expression.Constant(99.99m, decimal)</c>
/// directly — bypassing Jint's runtime which would otherwise marshal the decimal
/// back to a JS <c>number</c> and lose precision.
/// </para>
///
/// <para>
/// The runtime registration here exists so plain JS scripts (and generated
/// <c>.d.ts</c> types) don't break on <c>typeof linq === 'object'</c>; the actual
/// precision work happens in the translator.
/// </para>
/// </summary>
/// <example>
/// <code>
/// users.where(u => u.Price      > linq.decimal('99.99'))
/// users.where(u => u.ExternalId === linq.long('9007199254740993'))
/// </code>
/// </example>
public static class LinqCasts
{
    /// <summary>
    /// Registers the full <c>linq</c> global on the given engine:
    /// <c>decimal</c>, <c>double</c>, <c>int</c>, <c>long</c>.
    /// All parsers use <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    /// <remarks>
    /// This is called automatically by <c>JsEvalBuilder.AddLinq()</c>; you only
    /// need to call this explicitly if you're using the translator without the
    /// builder.
    /// </remarks>
    public static void Register(Jint.Engine engine)
    {
        // Bind a single POCO as the `linq` global. Jint exposes its public
        // instance methods as JS properties via reflection, so scripts call
        // `linq.guid('…')`, `linq.decimal('…')` etc. the same way they did
        // with the previous inline `var linq = { ... }` shim — but now the
        // single source of truth is the LinqGlobal class, which LinqTsContributor
        // reflects over to emit linq.d.ts (no hand-written .d.ts to drift from).
        engine.SetValue("linq", new LinqGlobal());
    }

    // --- numeric ---

    internal static decimal ParseDecimal(string s) =>
        decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    internal static double ParseDouble(string s) =>
        double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    internal static int ParseInt(string s) =>
        int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    internal static long ParseLong(string s) =>
        long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    // --- date/time ---

    /// <summary>Parses an ISO-8601 string into <see cref="DateTime"/> with <see cref="DateTimeKind.Unspecified"/>.</summary>
    internal static DateTime ParseDate(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Parses an ISO-8601 string into <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>.</summary>
    internal static DateTime ParseDateUtc(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>Parses an ISO-8601 string into <see cref="DateTimeOffset"/>.</summary>
    internal static DateTimeOffset ParseDateOffset(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>Parses an ISO-8601 date into <see cref="DateOnly"/>.</summary>
    internal static DateOnly ParseDateOnly(string s) =>
        DateOnly.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>Parses an ISO-8601 time into <see cref="TimeOnly"/>.</summary>
    internal static TimeOnly ParseTimeOnly(string s) =>
        TimeOnly.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>Parses a duration (.NET or ISO-8601 timespan) into <see cref="TimeSpan"/>.</summary>
    internal static TimeSpan ParseTimeSpan(string s) =>
        TimeSpan.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>Parses a string into <see cref="Guid"/> (any standard format — D/N/B/P/X).</summary>
    internal static Guid ParseGuid(string s) => Guid.Parse(s);
}
