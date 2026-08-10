using System;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Jint;
using Jint.Native;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.Engine;

/// <summary>
/// The bridge that re-exports a CLR module's methods as ES-module functions has
/// to marshal arguments and failures without changing their meaning. Both cases
/// pinned here used to be lost in translation.
/// </summary>
public class ModuleBridgeTests
{
    public sealed class ProbeModule : IJsModule
    {
        /// <summary>Takes the JS value itself — a rule translator needs the AST.</summary>
        public string Describe(JsValue value) => value switch
        {
            null => "null",
            Jint.Native.Function.Function => "callable",
            JsString s => $"string:{s.ToString()}",
            _ => value.Type.ToString()
        };

        public string Reject(string key) =>
            throw new ArgumentException($"unknown setting '{key}'");

        public int Add(int a, int b) => a + b;
    }

    private static JsEngine CreateEngine()
    {
        var sc = new ServiceCollection();
        sc.AddJsEval(b => b.AddModule<ProbeModule>());
        return sc.BuildServiceProvider().GetRequiredService<JsEngine>();
    }

    [Fact]
    public async Task JsValueParameter_ReceivesTheValueItself_NotItsClrProjection()
    {
        // ToObject() turns a JS function into a delegate, so a module declaring
        // a JsValue parameter used to get a Func<...> it could not accept and
        // the call failed outright. A rule arrives as a function and its AST is
        // the whole point, so it has to cross intact.
        using var engine = CreateEngine();

        await engine.ExecuteAsync("""
            import * as probe from 'probe';
            export const shape = probe.Describe(u => u.Age > 18);
            """);

        Assert.Equal("callable", engine.GetValue<string>("shape"));
    }

    [Fact]
    public async Task OrdinaryArguments_StillMarshalToTheirClrTypes()
    {
        using var engine = CreateEngine();

        await engine.ExecuteAsync("""
            import * as probe from 'probe';
            export const sum = probe.Add(2, 3);
            export const text = probe.Describe('hello');
            """);

        Assert.Equal(5, engine.GetValue<int>("sum"));
        Assert.Equal("string:hello", engine.GetValue<string>("text"));
    }

    [Fact]
    public async Task ModuleException_KeepsItsOwnMessage()
    {
        // Reflection wraps the module's exception. Unwrapped, the script only
        // saw "Exception has been thrown by the target of an invocation" and the
        // module's own validation message — the reason — was lost.
        using var engine = CreateEngine();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => engine.ExecuteAsync("""
            import * as probe from 'probe';
            export const never = probe.Reject('../../etc/passwd');
            """));

        Assert.Contains("unknown setting", Flatten(ex), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("target of an invocation", Flatten(ex), StringComparison.OrdinalIgnoreCase);
    }

    private static string Flatten(Exception ex)
    {
        var text = new System.Text.StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
            text.Append(e.Message).Append(" | ");
        return text.ToString();
    }
}
