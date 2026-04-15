using System;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Module.Common;
using Cocoar.JsEval.Module.Http;
using Cocoar.JsEval.Module.Logging;
using Cocoar.JsEval.TypeScript;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JsEval.Tests.TypeScript;

public class UnitTest1
{
    private IServiceProvider ServiceProvider { get; }
    private readonly ITestOutputHelper _output;

    public UnitTest1(ITestOutputHelper output)
    {
        _output = output;

        var sc = new ServiceCollection();
        sc.AddJsEval(b => b
            .AddModule<HttpModule>()
            .AddModule<CommonModule>()
            .AddModule<LoggingModule>()
        );
        sc.AddTsTranspiler();
        sc.AddLogging(c => c.AddConsole());

        ServiceProvider = sc.BuildServiceProvider();
    }

    [Fact]
    public async Task Test1()
    {
        var transpiler = ServiceProvider.GetRequiredService<TsTranspiler>();
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        var tsScript = @"
const a = 1;
const b = 2;
export const c = a + b;
";

        var jsScript = transpiler.Transpile(tsScript);
        await jsEngine.ExecuteAsync(jsScript);

        var result = jsEngine.GetValue<int>("c");
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task TryToGetFunctionAndInvokeIt()
    {
        var transpiler = ServiceProvider.GetRequiredService<TsTranspiler>();
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        var tsScript = @"
export function myFunc(name: string, age: number) {
    return `${name}:${age}`
}
";

        var jsScript = transpiler.Transpile(tsScript);
        await jsEngine.ExecuteAsync(jsScript);

        var func = jsEngine.GetFunction("myFunc");
        Assert.NotNull(func);
        var f1 = func.Invoke("Bernhard", 41);
        var t = jsEngine.InvokeFunction("myFunc", "Bernhard", 41);
    }

    [Fact]
    public async Task LoggingTest()
    {
        var transpiler = ServiceProvider.GetRequiredService<TsTranspiler>();
        using var jsEngine = ServiceProvider.GetRequiredService<JsEngine>();

        var tsScript = @"
import * as logger from 'logging'

logger.Info(99, 'TestMessage!!!');
";

        var jsScript = transpiler.Transpile(tsScript);
        await jsEngine.ExecuteAsync(jsScript);
    }
}
