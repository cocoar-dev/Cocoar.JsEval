using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using Cocoar.JsEval.Linq;
using Jint;
using Jint.Native;

namespace JsEval.Benchmarks;

/// <summary>
/// Benchmarks <see cref="JsExpressionTranslator"/> — measures the cost of
/// turning a JS arrow function (already parsed by Jint) into a .NET
/// <see cref="Expression{TDelegate}"/> tree.
///
/// Scenarios are split into "cold" (parse the JS each iteration) and "warm"
/// (JsFunction pre-cached — realistic for long-lived engines that re-translate
/// the same predicate repeatedly).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class LinqTranslatorBenchmarks
{
    public class DocUser
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsActive { get; set; }
        public int Age { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    // Reusable engines (simulates long-lived service)
    private Jint.Engine _engineSimple = default!;
    private Jint.Engine _engineMember = default!;
    private Jint.Engine _engineComplex = default!;
    private Jint.Engine _engineArray = default!;
    private Jint.Engine _engineCsDate = default!;

    // Pre-parsed JsFunctions for warm benchmarks
    private JsValue _fnSimple = default!;
    private JsValue _fnMember = default!;
    private JsValue _fnComplex = default!;
    private JsValue _fnArray = default!;
    private JsValue _fnCsDate = default!;

    // Scripts for cold benchmarks
    private const string SrcSimple  = "(u) => u.IsActive";
    private const string SrcMember  = "(u) => u.Name.startsWith('A')";
    private const string SrcComplex = "(u) => u.Name.startsWith('A') && u.IsActive && u.Age > 18";
    private const string SrcArray   = "(u) => u.Tags.some(t => t === 'vip')";
    private const string SrcCsDate  = "(u) => u.CreatedAt > cutoff.AddDays(-7)";

    [GlobalSetup]
    public void Setup()
    {
        _engineSimple  = NewEngine(); _fnSimple  = _engineSimple.Evaluate(SrcSimple);
        _engineMember  = NewEngine(); _fnMember  = _engineMember.Evaluate(SrcMember);
        _engineComplex = NewEngine(); _fnComplex = _engineComplex.Evaluate(SrcComplex);
        _engineArray   = NewEngine(); _fnArray   = _engineArray.Evaluate(SrcArray);

        _engineCsDate = NewEngine();
        _engineCsDate.SetValue("cutoff", new Cocoar.JsEval.Engine.CsDateTime(DateTime.UtcNow));
        _fnCsDate = _engineCsDate.Evaluate(SrcCsDate);
    }

    private static Jint.Engine NewEngine() => new();

    // --- WARM: JS already parsed; only translate ---

    [Benchmark(Description = "Warm: simple boolean property")]
    public Expression<Func<DocUser, bool>> Warm_Simple() =>
        JsExpressionTranslator.Translate<DocUser, bool>(_fnSimple, _engineSimple);

    [Benchmark(Description = "Warm: string method (startsWith)")]
    public Expression<Func<DocUser, bool>> Warm_Member() =>
        JsExpressionTranslator.Translate<DocUser, bool>(_fnMember, _engineMember);

    [Benchmark(Description = "Warm: complex three-clause && predicate")]
    public Expression<Func<DocUser, bool>> Warm_Complex() =>
        JsExpressionTranslator.Translate<DocUser, bool>(_fnComplex, _engineComplex);

    [Benchmark(Description = "Warm: nested lambda (array.some)")]
    public Expression<Func<DocUser, bool>> Warm_Array() =>
        JsExpressionTranslator.Translate<DocUser, bool>(_fnArray, _engineArray);

    [Benchmark(Description = "Warm: CsDateTime + implicit-op + AddDays")]
    public Expression<Func<DocUser, bool>> Warm_CsDateTime() =>
        JsExpressionTranslator.Translate<DocUser, bool>(_fnCsDate, _engineCsDate);

    // --- COLD: re-parse each iteration (fresh engine would be different, this
    //     reuses the engine but re-evaluates the script to get a fresh JsFunction) ---

    [Benchmark(Description = "Cold: parse + translate complex")]
    public Expression<Func<DocUser, bool>> Cold_Complex()
    {
        var fn = _engineComplex.Evaluate(SrcComplex);
        return JsExpressionTranslator.Translate<DocUser, bool>(fn, _engineComplex);
    }
}
