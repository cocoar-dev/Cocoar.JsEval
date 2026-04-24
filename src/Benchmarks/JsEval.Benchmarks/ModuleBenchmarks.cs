using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Module.Common;
using Cocoar.JsEval.Module.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JsEval.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ModuleBenchmarks
{
    private ServiceProvider _noModulesSp = null!;
    private ServiceProvider _commonModuleSp = null!;
    private ServiceProvider _threeModulesSp = null!;
    private JsPreparedModule _preparedImportScript = null!;
    private JsPreparedModule _preparedPooledImportScript = null!;
    private JsEngine _pooledImportEngine = null!;

    private const string SimpleScript = "export const x = 2 + 3;";

    private const string ImportScript = @"
import * as common from 'common'
export const guid = common.Guid.New().toString();
";

    [GlobalSetup]
    public void GlobalSetup()
    {
        var scNone = new ServiceCollection();
        scNone.AddJsEval();
        _noModulesSp = scNone.BuildServiceProvider();

        var scCommon = new ServiceCollection();
        scCommon.AddJsEval(b => b.AddModule<CommonModule>());
        _commonModuleSp = scCommon.BuildServiceProvider();

        var scThree = new ServiceCollection();
        scThree.AddJsEval(b => b
            .AddModule<CommonModule>()
            .AddModule<HttpModule>()
        );
        _threeModulesSp = scThree.BuildServiceProvider();

        _preparedImportScript = JsEngine.PrepareModule(ImportScript);
        _preparedPooledImportScript = JsEngine.PrepareModule(ImportScript);
        _pooledImportEngine = _commonModuleSp.GetRequiredService<JsEngine>();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _pooledImportEngine.Dispose();
        _noModulesSp.Dispose();
        _commonModuleSp.Dispose();
        _threeModulesSp.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Execute script without any modules registered")]
    public async Task<int?> NoModules()
    {
        using var scope = _noModulesSp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync(SimpleScript);
        return engine.GetValue<int>("x");
    }

    [Benchmark(Description = "Execute with Common module registered (not imported)")]
    public async Task<int?> WithCommonModule()
    {
        using var scope = _commonModuleSp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync(SimpleScript);
        return engine.GetValue<int>("x");
    }

    [Benchmark(Description = "Execute with 3 modules registered (not imported)")]
    public async Task<int?> WithThreeModules()
    {
        using var scope = _threeModulesSp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync(SimpleScript);
        return engine.GetValue<int>("x");
    }

    [Benchmark(Description = "Actually import and use Common module")]
    public async Task<string?> ImportModule()
    {
        using var scope = _commonModuleSp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync(ImportScript);
        return engine.GetValue<string>("guid");
    }

    [Benchmark(Description = "Import module via PrepareModule (fresh engine)")]
    public async Task<string?> ImportModulePrepared()
    {
        using var scope = _commonModuleSp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
        await engine.ExecuteAsync(_preparedImportScript);
        return engine.GetValue<string>("guid");
    }

    [Benchmark(Description = "Import module via ExecuteAsync(string) — pooled engine (hot loop)")]
    public async Task<string?> ImportModulePooled()
    {
        await _pooledImportEngine.ExecuteAsync(ImportScript);
        return _pooledImportEngine.GetValue<string>("guid");
    }

    [Benchmark(Description = "Import module via PrepareModule — pooled engine (hot loop)")]
    public async Task<string?> ImportModulePreparedPooled()
    {
        await _pooledImportEngine.ExecuteAsync(_preparedPooledImportScript);
        return _pooledImportEngine.GetValue<string>("guid");
    }

    [Benchmark(Description = "Cost of registering modules via builder")]
    public ServiceProvider ModuleRegistration()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b
            .AddModule<CommonModule>()
            .AddModule<HttpModule>()
        );
        var sp = sc.BuildServiceProvider();
        return sp;
    }
}
