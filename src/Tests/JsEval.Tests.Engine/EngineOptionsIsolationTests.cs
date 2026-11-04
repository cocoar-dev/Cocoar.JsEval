using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.JsEval.Engine;
using Jint;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// <see cref="JsEngineOptions"/> is a DI singleton shared by every scoped
/// <see cref="JsEngine"/>. Each engine must get its own Jint configuration —
/// constructing one must not mutate state another engine reads, and the
/// per-execution limits (timeout, statement budget, cancellation) of one engine
/// must not be reset or cancelled by another.
/// </summary>
public class EngineOptionsIsolationTests
{
    private static ServiceProvider CreateRoot(Action<JsEvalBuilder> configure)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(configure);
        return sc.BuildServiceProvider();
    }

    private static JsEngine Resolve(IServiceProvider root) =>
        root.CreateScope().ServiceProvider.GetRequiredService<JsEngine>();

    [Fact]
    public void ParallelConstruction_NeverYieldsABrokenEngine()
    {
        using var root = CreateRoot(b => b.WithExecutionTimeout(TimeSpan.FromSeconds(5)));
        var failures = new ConcurrentBag<Exception>();

        for (var burst = 0; burst < 5; burst++)
        {
            Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 32 }, _ =>
            {
                try
                {
                    using var scope = root.CreateScope();
                    var engine = scope.ServiceProvider.GetRequiredService<JsEngine>();
                    engine.Evaluate("var x = [1, 2, 3].map(v => v * 2);");
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            });
        }

        Assert.True(failures.IsEmpty,
            $"{failures.Count} parallel engines failed; first: {failures.FirstOrDefault()}");

        // A construction race that leaves the shared configuration corrupted
        // would break every later engine, not just the concurrent ones.
        using var after = Resolve(root);
        Assert.Equal(6, after.EvaluateExpression("[1, 2, 3].map(v => v * 2)[2]").AsNumber());
    }

    [Fact]
    public async Task ExecutionTimeout_HoldsPerEvaluation_WhileAnotherEngineEvaluates()
    {
        using var root = CreateRoot(b => b
            .WithExecutionTimeout(TimeSpan.FromMilliseconds(300))
            .WithMaxStatements(0));

        using var longRunning = Resolve(root);
        using var other = Resolve(root);

        using var stop = new CancellationTokenSource();
        var pinger = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                other.Evaluate("var y = 1;");
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);

        var sw = Stopwatch.StartNew();
        try
        {
            Assert.Throws<TimeoutException>(() =>
                longRunning.Evaluate("var t = Date.now(); while (Date.now() - t < 5000) {}"));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
                $"Timeout of 300 ms fired only after {sw.ElapsedMilliseconds} ms.");
        }
        finally
        {
            await stop.CancelAsync();
            await pinger;
        }
    }

    [Fact]
    public async Task MaxStatements_HoldsPerEvaluation_WhileAnotherEngineEvaluates()
    {
        using var root = CreateRoot(b => b
            .WithExecutionTimeout(TimeSpan.FromSeconds(30))
            .WithMaxStatements(100_000));

        using var longRunning = Resolve(root);
        using var other = Resolve(root);

        using var stop = new CancellationTokenSource();
        var pinger = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                other.Evaluate("var y = 1;");
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            Assert.Throws<StatementsCountOverflowException>(() =>
                longRunning.Evaluate("var i = 0; while (i < 50000000) { i++; }"));
        }
        finally
        {
            await stop.CancelAsync();
            await pinger;
        }
    }

    // Jint checks cancellation every 64 statements, not on every one, so the
    // scripts below loop long enough to be sure the token is observed.
    private const string Loop = "var s = 0; for (var i = 0; i < 1000; i++) { s += i; } s";

    [Fact]
    public void Stop_CancelsOnlyItsOwnEngine_WhenEnginesAreBuiltConcurrently()
    {
        using var root = CreateRoot(b => b.WithMaxStatements(0));
        var engines = new JsEngine[1000];

        // Build the engines concurrently: that is when one engine could pick up
        // the cancellation token another engine registered on shared options.
        Parallel.For(0, engines.Length, new ParallelOptions { MaxDegreeOfParallelism = 32 },
            i => engines[i] = Resolve(root));

        try
        {
            for (var i = 0; i < engines.Length; i += 2)
                engines[i].Stop();

            for (var i = 0; i < engines.Length; i++)
            {
                if (i % 2 == 0)
                    Assert.Throws<ExecutionCanceledException>(() => engines[i].EvaluateExpression(Loop));
                else
                    Assert.Equal(499_500, engines[i].EvaluateExpression(Loop).AsNumber());
            }
        }
        finally
        {
            foreach (var engine in engines)
                engine?.Dispose();
        }
    }

    [Fact]
    public void Stop_OnOneEngine_DoesNotCancelAnother()
    {
        using var root = CreateRoot(b => b.WithMaxStatements(0));
        using var a = Resolve(root);
        using var b = Resolve(root);

        a.Stop();

        Assert.Throws<ExecutionCanceledException>(() => a.EvaluateExpression(Loop));
        Assert.Equal(499_500, b.EvaluateExpression(Loop).AsNumber());
    }
}
