using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Jint;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Linq;

/// <summary>
/// The asynchronous terminals exist because Marten 9 refuses synchronous
/// execution. Their fallback path — a provider with no async terminal at all,
/// such as an in-memory <c>IQueryable</c> — has to keep working, and that is
/// what these tests cover. The Marten side is covered against a real database
/// in <c>JsEval.Tests.Marten</c>.
/// </summary>
public class AsyncTerminalTests
{
    public sealed class User
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }

    private static readonly User[] People =
    [
        new() { Name = "Alice", Age = 34, IsActive = true },
        new() { Name = "Bob",   Age = 17, IsActive = true },
        new() { Name = "Cara",  Age = 41, IsActive = false },
    ];

    private static JsEngine CreateEngine(out IQueryable<User> source)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b.AddLinq());
        var engine = sc.BuildServiceProvider().GetRequiredService<JsEngine>();
        source = People.AsQueryable();
        engine.SetValue("users", source);
        return engine;
    }

    [Fact]
    public async Task CountAsync_IsAwaitableFromScript()
    {
        using var engine = CreateEngine(out _);
        using (JsLinqContext.Scope(engine.UnderlyingEngine))
        {
            await engine.ExecuteAsync("export const n = await users.countAsync(u => u.IsActive);");
            Assert.Equal(2, engine.GetValue<int>("n"));
        }
    }

    [Fact]
    public async Task AnyAsync_IsAwaitableFromScript()
    {
        using var engine = CreateEngine(out _);
        using (JsLinqContext.Scope(engine.UnderlyingEngine))
        {
            await engine.ExecuteAsync("""
                export const old = await users.anyAsync(u => u.Age > 40);
                export const ancient = await users.anyAsync(u => u.Age > 100);
                """);

            Assert.True(engine.GetValue<bool>("old"));
            Assert.False(engine.GetValue<bool>("ancient"));
        }
    }

    [Fact]
    public async Task FindAsync_IsAwaitableFromScript()
    {
        using var engine = CreateEngine(out _);
        using (JsLinqContext.Scope(engine.UnderlyingEngine))
        {
            await engine.ExecuteAsync("""
                const bob = await users.findAsync(u => u.Name === 'Bob');
                export const probe = bob.Name + '|' + bob.Age;
                """);

            Assert.Equal("Bob|17", engine.GetValue<string>("probe"));
        }
    }

    [Fact]
    public async Task NoPredicate_CountsEverything()
    {
        using var engine = CreateEngine(out _);
        using (JsLinqContext.Scope(engine.UnderlyingEngine))
        {
            // No parameterless overload exists on purpose — it would outrank the
            // provider's own CountAsync in ordinary C#. `null` means "no predicate".
            await engine.ExecuteAsync("export const n = await users.countAsync(null);");
            Assert.Equal(3, engine.GetValue<int>("n"));
        }
    }

    [Fact]
    public async Task AsyncAndSyncTerminals_AgreeOnTheSameProvider()
    {
        // On a provider that permits both, the async terminal is the synchronous
        // one behind a completed Task — same rule, same answer.
        using var engine = CreateEngine(out _);
        using (JsLinqContext.Scope(engine.UnderlyingEngine))
        {
            await engine.ExecuteAsync("""
                export const sync = users.count(u => u.IsActive);
                export const async_ = await users.countAsync(u => u.IsActive);
                """);

            Assert.Equal(engine.GetValue<int>("sync"), engine.GetValue<int>("async_"));
        }
    }

    [Fact]
    public async Task ChainsAfterWhereAndAfterOrderBy()
    {
        using var engine = CreateEngine(out _);
        using (JsLinqContext.Scope(engine.UnderlyingEngine))
        {
            await engine.ExecuteAsync("""
                export const n = await users.where(u => u.Age > 18).countAsync(null);
                const first = await users.orderBy(u => u.Age).findAsync(null);
                export const youngest = first.Name;
                """);

            Assert.Equal(2, engine.GetValue<int>("n"));
            Assert.Equal("Bob", engine.GetValue<string>("youngest"));
        }
    }

    [Fact]
    public void TerminalsAfterWhereThenOrderBy_AreUnavailable_SameAsSynchronous()
    {
        // Pins a pre-existing limitation rather than a new one: chaining an
        // ordering onto a `where` result loses the terminal methods entirely.
        // The synchronous `find` disappears exactly as `findAsync` does, so the
        // async additions introduce no asymmetry of their own.
        using var engine = CreateEngine(out _);
        using (JsLinqContext.Scope(engine.UnderlyingEngine))
        {
            Assert.Equal("undefined", engine.EvaluateExpression(
                "typeof users.where(u => u.IsActive).orderBy(u => u.Age).find").AsString());
            Assert.Equal("undefined", engine.EvaluateExpression(
                "typeof users.where(u => u.IsActive).orderBy(u => u.Age).findAsync").AsString());

            // Either one alone is fine.
            Assert.Equal("function", engine.EvaluateExpression(
                "typeof users.orderBy(u => u.Age).findAsync").AsString());
            Assert.Equal("function", engine.EvaluateExpression(
                "typeof users.where(u => u.IsActive).findAsync").AsString());
        }
    }
}
