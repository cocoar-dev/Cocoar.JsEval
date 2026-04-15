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
    private JsEngine? _engine;

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
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _noModulesSp.Dispose();
        _commonModuleSp.Dispose();
        _threeModulesSp.Dispose();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _engine?.Dispose();
        _engine = null;
    }

    [Benchmark(Baseline = true, Description = "Execute script without any modules registered")]
    public async Task<int?> NoModules()
    {
        _engine = _noModulesSp.GetRequiredService<JsEngine>();
        await _engine.ExecuteAsync(SimpleScript);
        return _engine.GetValue<int>("x");
    }

    [Benchmark(Description = "Execute with Common module registered (not imported)")]
    public async Task<int?> WithCommonModule()
    {
        _engine = _commonModuleSp.GetRequiredService<JsEngine>();
        await _engine.ExecuteAsync(SimpleScript);
        return _engine.GetValue<int>("x");
    }

    [Benchmark(Description = "Execute with 3 modules registered (not imported)")]
    public async Task<int?> WithThreeModules()
    {
        _engine = _threeModulesSp.GetRequiredService<JsEngine>();
        await _engine.ExecuteAsync(SimpleScript);
        return _engine.GetValue<int>("x");
    }

    [Benchmark(Description = "Actually import and use Common module")]
    public async Task<string?> ImportModule()
    {
        _engine = _commonModuleSp.GetRequiredService<JsEngine>();
        await _engine.ExecuteAsync(ImportScript);
        return _engine.GetValue<string>("guid");
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
