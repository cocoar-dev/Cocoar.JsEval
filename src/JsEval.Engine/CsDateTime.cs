using System;
using System.Globalization;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// A thin <see cref="DateTime"/> wrapper exposed to JS so that .NET date
/// arithmetic (<see cref="DateTime.AddDays"/>, properties like <c>Year</c>, …)
/// is callable from scripts without Jint converting the value to a JS
/// <c>Date</c> (which would otherwise strip the CLR methods).
///
/// <para>
/// Uses PascalCase method names intentionally — the idea is that users see "this
/// is a C# type, I'm calling C# methods on it". Jint's CLR interop also allows
/// camelCase (<c>d.addDays(7)</c>), so both work.
/// </para>
///
/// <para>
/// Implicit conversions to/from <see cref="DateTime"/> make this a drop-in
/// wrapper: closures set via <c>engine.SetValue("x", new CsDateTime(dt))</c>
/// compare correctly against <c>DateTime</c> columns in LINQ predicates —
/// provided the translator resolves implicit-op conversions (it does).
/// </para>
/// </summary>
public sealed class CsDateTime : IComparable<CsDateTime>, IEquatable<CsDateTime>
{
    public DateTime Value { get; }

    public CsDateTime(DateTime value) { Value = value; }

    // --- static factories ---

    public static CsDateTime Now     => new(DateTime.Now);
    public static CsDateTime UtcNow  => new(DateTime.UtcNow);
    public static CsDateTime Today   => new(DateTime.Today);

    public static CsDateTime Parse(string s) =>
        new(DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None));
    public static CsDateTime ParseUtc(string s) =>
        new(DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
    public static CsDateTime From(int year, int month, int day) =>
        new(new DateTime(year, month, day));
    public static CsDateTime From(int year, int month, int day, int hour, int minute, int second) =>
        new(new DateTime(year, month, day, hour, minute, second));
    public static CsDateTime FromUnixSeconds(long seconds) =>
        new(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime);
    public static CsDateTime FromUnixMilliseconds(long ms) =>
        new(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime);

    // --- arithmetic (mirror DateTime's signatures) ---

    public CsDateTime AddTicks(long ticks)           => new(Value.AddTicks(ticks));
    public CsDateTime AddMilliseconds(double value)  => new(Value.AddMilliseconds(value));
    public CsDateTime AddSeconds(double value)       => new(Value.AddSeconds(value));
    public CsDateTime AddMinutes(double value)       => new(Value.AddMinutes(value));
    public CsDateTime AddHours(double value)         => new(Value.AddHours(value));
    public CsDateTime AddDays(double value)          => new(Value.AddDays(value));
    public CsDateTime AddMonths(int months)          => new(Value.AddMonths(months));
    public CsDateTime AddYears(int years)            => new(Value.AddYears(years));

    public TimeSpan   Subtract(CsDateTime other)     => Value - other.Value;
    public TimeSpan   Subtract(DateTime other)       => Value - other;
    public CsDateTime Subtract(TimeSpan span)        => new(Value - span);

    public CsDateTime ToUniversalTime() => new(Value.ToUniversalTime());
    public CsDateTime ToLocalTime()     => new(Value.ToLocalTime());

    // --- date-component properties (mirror DateTime) ---

    public int       Year        => Value.Year;
    public int       Month       => Value.Month;
    public int       Day         => Value.Day;
    public int       Hour        => Value.Hour;
    public int       Minute      => Value.Minute;
    public int       Second      => Value.Second;
    public int       Millisecond => Value.Millisecond;
    public int       DayOfYear   => Value.DayOfYear;
    public DayOfWeek DayOfWeek   => Value.DayOfWeek;
    public DateTimeKind Kind     => Value.Kind;
    public long      Ticks       => Value.Ticks;

    // --- formatting ---

    public override string ToString() =>
        Value.ToString("O", CultureInfo.InvariantCulture);
    public string ToString(string format) =>
        Value.ToString(format, CultureInfo.InvariantCulture);
    public string ToIsoString() =>
        Value.Kind == DateTimeKind.Utc
            ? Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
            : Value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

    // --- implicit conversions — the key for predicate compatibility ---

    public static implicit operator DateTime(CsDateTime wrapper) => wrapper.Value;
    public static implicit operator CsDateTime(DateTime value)   => new(value);

    // --- comparison operators ---

    public static bool operator ==(CsDateTime? a, CsDateTime? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.Value == b.Value);
    public static bool operator !=(CsDateTime? a, CsDateTime? b) => !(a == b);
    public static bool operator <(CsDateTime a, CsDateTime b)  => a.Value <  b.Value;
    public static bool operator <=(CsDateTime a, CsDateTime b) => a.Value <= b.Value;
    public static bool operator >(CsDateTime a, CsDateTime b)  => a.Value >  b.Value;
    public static bool operator >=(CsDateTime a, CsDateTime b) => a.Value >= b.Value;

    public int CompareTo(CsDateTime? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public bool Equals(CsDateTime? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is CsDateTime o && Equals(o);
    public override int GetHashCode() => Value.GetHashCode();

    // --- fluent checks (nice sugar) ---

    public bool IsBefore(CsDateTime other) => Value <  other.Value;
    public bool IsAfter(CsDateTime other)  => Value >  other.Value;
    public bool IsBefore(DateTime other)   => Value <  other;
    public bool IsAfter(DateTime other)    => Value >  other;
}
