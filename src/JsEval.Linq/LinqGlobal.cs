namespace Cocoar.JsEval.Linq;

/// <summary>
/// Concrete backing for the <c>linq</c> JS global. Registered on the Jint engine
/// via <see cref="LinqCasts.Register"/>; scripts access it as
/// <c>linq.decimal('99.99')</c>, <c>linq.guid('…')</c>, etc.
/// <para>
/// This type is also the single source of truth <see cref="LinqTsContributor"/>
/// reflects over to emit <c>linq.d.ts</c> — no hand-written .d.ts to drift from
/// the runtime.
/// </para>
/// <para>
/// <b>Precision is guaranteed at translation time, not runtime.</b> When
/// <see cref="JsExpressionTranslator"/> encounters <c>linq.decimal('99.99')</c>
/// inside a predicate, it pattern-matches the AST and emits a typed
/// <c>Expression.Constant(99.99m, decimal)</c> directly — Jint's marshalling
/// never rounds the value. The instance methods here exist so plain JS scripts
/// outside a predicate context don't break on <c>typeof linq === 'object'</c>;
/// in that case they run and parse normally (losing decimal-level precision,
/// which is inherent to JS's IEEE-754 number type).
/// </para>
/// </summary>
#pragma warning disable CA1720 // Identifier contains type name — JS-facing method names on a JS global object
#pragma warning disable CA1822 // Instance methods required by Jint ObjectWrapper + LinqTsContributor reflection
public sealed class LinqGlobal
{
    /// <summary>Parse as <see cref="decimal"/>. Precision preserved at translation time.</summary>
    public decimal @decimal(string value) => LinqCasts.ParseDecimal(value);

    /// <summary>Parse as <see cref="double"/> (IEEE-754 double).</summary>
    public double @double(string value) => LinqCasts.ParseDouble(value);

    /// <summary>Parse as <see cref="int"/>.</summary>
    public int @int(string value) => LinqCasts.ParseInt(value);

    /// <summary>Parse as <see cref="long"/>. Full int64 range preserved at translation time.</summary>
    public long @long(string value) => LinqCasts.ParseLong(value);

    /// <summary>Parse as <see cref="DateTime"/> (unspecified kind).</summary>
    public DateTime date(string value) => LinqCasts.ParseDate(value);

    /// <summary>Parse as <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>.</summary>
    public DateTime dateUtc(string value) => LinqCasts.ParseDateUtc(value);

    /// <summary>Parse as <see cref="DateTimeOffset"/>.</summary>
    public DateTimeOffset dateOffset(string value) => LinqCasts.ParseDateOffset(value);

    /// <summary>Parse as <see cref="DateOnly"/> (date portion only).</summary>
    public DateOnly dateOnly(string value) => LinqCasts.ParseDateOnly(value);

    /// <summary>Parse as <see cref="TimeOnly"/>.</summary>
    public TimeOnly timeOnly(string value) => LinqCasts.ParseTimeOnly(value);

    /// <summary>Parse as <see cref="TimeSpan"/>.</summary>
    public TimeSpan timeSpan(string value) => LinqCasts.ParseTimeSpan(value);

    /// <summary>Parse as <see cref="Guid"/>.</summary>
    public Guid guid(string value) => LinqCasts.ParseGuid(value);

    /// <summary>Capture today's local date at the moment of evaluation.</summary>
    public DateTime today() => DateTime.Today;

    /// <summary>Capture the current local time at the moment of evaluation.</summary>
    public DateTime now() => DateTime.Now;

    /// <summary>Capture the current UTC time at the moment of evaluation.</summary>
    public DateTime utcNow() => DateTime.UtcNow;

    /// <summary>Capture today's UTC date at the moment of evaluation.</summary>
    public DateTime todayUtc() => DateTime.UtcNow.Date;
}
#pragma warning restore CA1822
#pragma warning restore CA1720
