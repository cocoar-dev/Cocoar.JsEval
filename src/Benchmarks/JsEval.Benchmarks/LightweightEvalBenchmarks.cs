using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Cocoar.JsEval.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace JsEval.Benchmarks;

/// <summary>
/// Benchmarks for lightweight script evaluation:
/// - CLR objects injected as globals via SetValue
/// - Short scripts call methods on those objects
/// - Evaluate path (no module system, no async)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 20)]
public class LightweightEvalBenchmarks
{
    // --- Simulated CLR objects that the script interacts with ---

    public class AccessContext
    {
        public Guid UserId { get; set; }
        public List<string> Permissions { get; set; } = [];
        public List<Guid> ManagedCustomerIds { get; set; } = [];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
    }

    public class QueryBuilder
    {
        private readonly List<string> _filters = [];
        public QueryBuilder WhereCustomerIn(object[] ids) { _filters.Add($"customer_in({ids.Length})"); return this; }
        public QueryBuilder WhereResponsible(Guid userId) { _filters.Add($"responsible={userId}"); return this; }
        public QueryBuilder WhereCreatedBy(Guid userId) { _filters.Add($"created_by={userId}"); return this; }
        public QueryBuilder WhereStatus(string status) { _filters.Add($"status={status}"); return this; }
        public QueryBuilder ExcludeArchived() { _filters.Add("not_archived"); return this; }
        public QueryBuilder WhereCritical() { _filters.Add("critical"); return this; }
        public QueryBuilder All() { _filters.Add("all"); return this; }
        public void Reset() => _filters.Clear();
        public int FilterCount => _filters.Count;
    }

    // --- Test data ---
    private AccessContext _ctx = null!;
    private QueryBuilder _query = null!;
    private IServiceProvider _sp = null!;

    // Scripts (raw strings)
    private const string SimpleScript = "query.WhereResponsible(ctx.UserId);";

    private const string MediumScript = """
        if (ctx.ManagedCustomerIds.Count > 0) {
            query.WhereCustomerIn(ctx.ManagedCustomerIds.ToArray());
        }
        query.WhereResponsible(ctx.UserId);
        """;

    private const string ComplexScript = """
        if (ctx.HasPermission('todo:view-all')) {
            query.All();
        } else {
            if (ctx.ManagedCustomerIds.Count > 0) {
                query.WhereCustomerIn(ctx.ManagedCustomerIds.ToArray());
            }
            query.WhereResponsible(ctx.UserId);
            query.ExcludeArchived();
        }
        """;

    // Pre-parsed scripts
    private JsPreparedScript _simpleScriptPrepared = null!;
    private JsPreparedScript _mediumScriptPrepared = null!;
    private JsPreparedScript _complexScriptPrepared = null!;

    // Reusable engine for pooled benchmarks
    private JsEngine _pooledEngine = null!;

    [GlobalSetup]
    public void Setup()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        _sp = sc.BuildServiceProvider();

        _ctx = new AccessContext
        {
            UserId = Guid.NewGuid(),
            Permissions = ["todo:view", "customer:view"],
            ManagedCustomerIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]
        };
        _query = new QueryBuilder();

        _simpleScriptPrepared = JsEngine.Prepare(SimpleScript);
        _mediumScriptPrepared = JsEngine.Prepare(MediumScript);
        _complexScriptPrepared = JsEngine.Prepare(ComplexScript);

        // Create a pooled engine (simulates engine reuse)
        _pooledEngine = _sp.GetRequiredService<JsEngine>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pooledEngine.Dispose();
    }

    // =================================================================
    // BASELINE: New engine per request + raw string script (worst case)
    // =================================================================

    [Benchmark(Description = "New engine + Evaluate(string) — simple")]
    public void NewEngine_String_Simple()
    {
        using var scope = _sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        engine.SetValue("ctx", _ctx);
        engine.SetValue("query", _query);
        engine.Evaluate(SimpleScript);
        _query.Reset();
    }

    [Benchmark(Description = "New engine + Evaluate(string) — complex")]
    public void NewEngine_String_Complex()
    {
        using var scope = _sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        engine.SetValue("ctx", _ctx);
        engine.SetValue("query", _query);
        engine.Evaluate(ComplexScript);
        _query.Reset();
    }

    // =================================================================
    // PREPARED: New engine per request + pre-parsed script
    // =================================================================

    [Benchmark(Description = "New engine + Evaluate(prepared) — simple")]
    public void NewEngine_Prepared_Simple()
    {
        using var scope = _sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        engine.SetValue("ctx", _ctx);
        engine.SetValue("query", _query);
        engine.Evaluate(_simpleScriptPrepared);
        _query.Reset();
    }

    [Benchmark(Description = "New engine + Evaluate(prepared) — complex")]
    public void NewEngine_Prepared_Complex()
    {
        using var scope = _sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        engine.SetValue("ctx", _ctx);
        engine.SetValue("query", _query);
        engine.Evaluate(_complexScriptPrepared);
        _query.Reset();
    }

    // =================================================================
    // POOLED: Reused engine + pre-parsed script (best case)
    // =================================================================

    [Benchmark(Description = "Pooled engine + Evaluate(prepared) — simple")]
    public void Pooled_Prepared_Simple()
    {
        _pooledEngine.SetValue("ctx", _ctx);
        _pooledEngine.SetValue("query", _query);
        _pooledEngine.Evaluate(_simpleScriptPrepared);
        _query.Reset();
    }

    [Benchmark(Description = "Pooled engine + Evaluate(prepared) — medium")]
    public void Pooled_Prepared_Medium()
    {
        _pooledEngine.SetValue("ctx", _ctx);
        _pooledEngine.SetValue("query", _query);
        _pooledEngine.Evaluate(_mediumScriptPrepared);
        _query.Reset();
    }

    [Benchmark(Description = "Pooled engine + Evaluate(prepared) — complex")]
    public void Pooled_Prepared_Complex()
    {
        _pooledEngine.SetValue("ctx", _ctx);
        _pooledEngine.SetValue("query", _query);
        _pooledEngine.Evaluate(_complexScriptPrepared);
        _query.Reset();
    }

    // =================================================================
    // COMPARISON: ExecuteAsync (module path) vs Execute (lightweight)
    // =================================================================

    [Benchmark(Description = "ExecuteAsync (module path) — simple")]
    public async Task ExecuteAsync_Simple()
    {
        using var scope = _sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        engine.SetValue("ctx", _ctx);
        engine.SetValue("query", _query);
        await engine.ExecuteAsync("query.WhereResponsible(ctx.UserId);");
        _query.Reset();
    }

    // =================================================================
    // OVERHEAD: Just measure Prepare() cost
    // =================================================================

    [Benchmark(Description = "Prepare(script) — parse cost")]
    public JsPreparedScript PrepareScript()
    {
        return JsEngine.Prepare(ComplexScript);
    }
}
