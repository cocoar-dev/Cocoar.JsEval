using Cocoar.JsEval.Linq;
using Jint;
using Xunit;

namespace Cocoar.JsEval.Tests.Linq;

/// <summary>
/// Runtime behaviour of <c>linq.*</c> — these are documentary. The real precision
/// guarantee is on the translator side (see TranslatorTests.Linq*).
/// </summary>
public class LinqCastsRuntimeTests
{
    private static Jint.Engine BuildEngine()
    {
        var engine = new Jint.Engine();
        LinqCasts.Register(engine);
        return engine;
    }

    [Fact]
    public void LinqDecimal_ValueParsedCorrectly_AtRuntime()
    {
        var engine = BuildEngine();
        // Jint marshals decimal → JS number when the function returns.
        var result = (double)engine.Evaluate("linq.decimal('99.99')").ToObject()!;
        Assert.Equal(99.99, result);
    }

    [Fact]
    public void LinqDouble_ParsesString()
    {
        var engine = BuildEngine();
        var result = (double)engine.Evaluate("linq.double('3.14159265358979')").ToObject()!;
        Assert.Equal(3.14159265358979d, result);
    }

    [Fact]
    public void LinqInt_ParsesString()
    {
        var engine = BuildEngine();
        var result = (double)engine.Evaluate("linq.int('42')").ToObject()!;
        Assert.Equal(42, result);
    }

    [Fact]
    public void LinqDecimal_UsesInvariantCulture_ExpectsDot()
    {
        var engine = BuildEngine();
        Assert.Throws<FormatException>(() => { _ = engine.Evaluate("linq.decimal('99,99')").ToObject(); });
    }

    [Fact]
    public void LinqInt_ThrowsOnInvalidInput()
    {
        var engine = BuildEngine();
        Assert.Throws<FormatException>(() => { _ = engine.Evaluate("linq.int('not a number')").ToObject(); });
    }

    // Note on date/time runtime behaviour:
    // When a CLR function returns DateTime/DateTimeOffset, Jint marshals the value
    // through its JS Date bridge — which applies the host's local timezone rules and
    // can shift the moment. That's why precision is only guaranteed when
    // linq.date/...  is used inline inside a query predicate — the translator then
    // intercepts the call at AST level and builds a typed Expression.Constant directly.

    [Fact]
    public void LinqDateUtc_CallableAtRuntime_RoundtripsAsUtc()
    {
        var engine = BuildEngine();
        var dt = (DateTime)engine.Evaluate("linq.dateUtc('2024-06-15T14:30:00Z')").ToObject()!;
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc), dt);
    }

    [Fact]
    public void WithoutRegister_LinqIsUndefined()
    {
        var engine = new Jint.Engine();
        var t = engine.Evaluate("typeof linq").AsString();
        Assert.Equal("undefined", t);
    }
}
