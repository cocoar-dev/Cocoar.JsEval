using System;
using Cocoar.JsEval.Engine;
using Jint;
using Xunit;

namespace JsEval.Tests.Engine;

public class CsDateTimeTests
{
    [Fact]
    public void AddDays_ReturnsNewInstance_WithSameKind()
    {
        var d = new CsDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var next = d.AddDays(7);
        Assert.Equal(new DateTime(2024, 1, 8, 0, 0, 0, DateTimeKind.Utc), next.Value);
        Assert.Equal(DateTimeKind.Utc, next.Kind);
    }

    [Fact]
    public void ImplicitConversion_CsDateTimeToDateTime()
    {
        var d = new CsDateTime(new DateTime(2024, 1, 1));
        DateTime dt = d; // implicit
        Assert.Equal(new DateTime(2024, 1, 1), dt);
    }

    [Fact]
    public void ImplicitConversion_DateTimeToCsDateTime()
    {
        CsDateTime d = new DateTime(2024, 1, 1); // implicit
        Assert.Equal(new DateTime(2024, 1, 1), d.Value);
    }

    [Fact]
    public void CompareOperators_WorkAsExpected()
    {
        var a = new CsDateTime(new DateTime(2024, 1, 1));
        var b = new CsDateTime(new DateTime(2024, 2, 1));
        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(a <= a);
        Assert.True(b >= b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void Factories_ProduceExpectedValues()
    {
        Assert.Equal(DateTimeKind.Utc, CsDateTime.UtcNow.Kind);
        Assert.Equal(new DateTime(2024, 6, 15), CsDateTime.From(2024, 6, 15).Value);
        Assert.Equal(new DateTime(2024, 6, 15, 14, 30, 0), CsDateTime.From(2024, 6, 15, 14, 30, 0).Value);
    }

    [Fact]
    public void Parse_ParsesIsoFormat()
    {
        var d = CsDateTime.Parse("2024-06-15T14:30:00");
        Assert.Equal(new DateTime(2024, 6, 15, 14, 30, 0), d.Value);
    }

    [Fact]
    public void ParseUtc_MarksKindUtc()
    {
        var d = CsDateTime.ParseUtc("2024-06-15T14:30:00Z");
        Assert.Equal(DateTimeKind.Utc, d.Kind);
    }

    [Fact]
    public void Subtract_CsDateTime_ReturnsTimeSpan()
    {
        var a = new CsDateTime(new DateTime(2024, 1, 8));
        var b = new CsDateTime(new DateTime(2024, 1, 1));
        Assert.Equal(TimeSpan.FromDays(7), a.Subtract(b));
    }

    [Fact]
    public void Properties_MirrorDateTime()
    {
        var d = new CsDateTime(new DateTime(2024, 6, 15, 14, 30, 45, 123));
        Assert.Equal(2024, d.Year);
        Assert.Equal(6, d.Month);
        Assert.Equal(15, d.Day);
        Assert.Equal(14, d.Hour);
        Assert.Equal(30, d.Minute);
        Assert.Equal(45, d.Second);
        Assert.Equal(123, d.Millisecond);
        Assert.Equal(DayOfWeek.Saturday, d.DayOfWeek);
    }

    [Fact]
    public void IsBefore_IsAfter_ReturnsExpected()
    {
        var a = new CsDateTime(new DateTime(2024, 1, 1));
        var b = new CsDateTime(new DateTime(2024, 2, 1));
        Assert.True(a.IsBefore(b));
        Assert.True(b.IsAfter(a));
    }

    [Fact]
    public void ToString_ProducesIsoFormat()
    {
        var d = new CsDateTime(new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc));
        var s = d.ToString();
        Assert.Contains("2024-06-15", s);
    }

    // --- Jint integration ---

    [Fact]
    public void Jint_AddDays_Callable_FromJs()
    {
        var engine = new Jint.Engine();
        CsDateTimeGlobals.Register(engine);
        engine.SetValue("d", new CsDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var result = engine.Evaluate("d.AddDays(7).Year * 10000 + d.AddDays(7).Month * 100 + d.AddDays(7).Day").AsNumber();
        Assert.Equal(20240108, (int)result);
    }

    [Fact]
    public void Jint_LowercaseMethodName_AlsoWorks()
    {
        // Jint's CLR interop is case-insensitive for type member access.
        var engine = new Jint.Engine();
        CsDateTimeGlobals.Register(engine);
        engine.SetValue("d", new CsDateTime(new DateTime(2024, 1, 1)));
        var day = engine.Evaluate("d.addDays(7).day").AsNumber();
        Assert.Equal(8, (int)day);
    }

    [Fact]
    public void Jint_StaticFactory_Callable()
    {
        var engine = new Jint.Engine();
        CsDateTimeGlobals.Register(engine);
        var year = engine.Evaluate("CsDateTime.From(2024, 6, 15).Year").AsNumber();
        Assert.Equal(2024, (int)year);
    }

    [Fact]
    public void Jint_UtcNow_Callable()
    {
        var engine = new Jint.Engine();
        CsDateTimeGlobals.Register(engine);
        // Check result has a reasonable Year (current year-ish)
        var year = engine.Evaluate("CsDateTime.UtcNow.Year").AsNumber();
        Assert.InRange((int)year, 2020, 2100);
    }
}
