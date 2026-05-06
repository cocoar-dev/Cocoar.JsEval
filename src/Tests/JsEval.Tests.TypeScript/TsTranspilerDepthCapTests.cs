using Cocoar.JsEval.Engine;
using Cocoar.JsEval.TypeScript;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.TypeScript;

/// <summary>
/// Pins the TS transpiler depth cap (F6b). Without this guard the embedded
/// TypeScript compiler — running as JavaScript inside Jint — exhausts the
/// .NET thread stack on deeply nested input (~300 levels) and crashes the
/// host process with an unrecoverable StackOverflowException, well before
/// Acornima's own 5000 cap engages.
/// </summary>
public class TsTranspilerDepthCapTests
{
    private static TsTranspiler CreateTranspiler(int? maxParseDepth = null)
    {
        var sc = new ServiceCollection();
        sc.AddJsEval();
        sc.AddTsTranspiler();
        var sp = sc.BuildServiceProvider();

        return maxParseDepth is null
            ? sp.GetRequiredService<TsTranspiler>()
            : new TsTranspiler { MaxParseDepth = maxParseDepth.Value };
    }

    private static string BuildNestedTernary(int depth)
    {
        var body = "true";
        for (var i = 0; i < depth; i++) body = $"({body} ? 1 : 2)";
        return $"const f = (p) => {body} === 1; f";
    }

    [Fact]
    public void Transpile_DepthBelowCap_Succeeds()
    {
        // 50 nested ternaries — well below the default 128 cap and the
        // ~300 SOE threshold. Must transpile cleanly.
        var ts = CreateTranspiler();

        var js = ts.Transpile(BuildNestedTernary(50));

        Assert.Contains("=== 1", js);
    }

    [Fact]
    public void Transpile_DepthAboveCap_ThrowsControlledException()
    {
        // 500 nested ternaries — without the guard this would crash the
        // host process (verified empirically; see security-F6b-tenant-admin-escalation.md).
        var ts = CreateTranspiler();

        var ex = Assert.Throws<TsTranspileException>(() =>
            ts.Transpile(BuildNestedTernary(500)));

        Assert.Contains("MaxParseDepth", ex.Message);
    }

    [Fact]
    public void Transpile_StringsAndCommentsDontCount()
    {
        // Source is shallow at the syntactic level — all the parens live
        // inside string literals or comments. With MaxParseDepth = 5 this
        // would wrongly trip if the scanner counted them.
        var ts = CreateTranspiler(maxParseDepth: 5);
        const string src = @"
            // ((((((((((((((((((((((((((((((((((((((((((((((((((
            /* (((((((((((((((((((((((((((((((((((((((((((((((((( */
            const a = '((((((((((((((((((((((((((((((((((((((((((((((((((' ;
            const b = ""((((((((((((((((((((((((((((((((((((((((((((((((((""  ;
            const c = `((((((((((((((((((((((((((((((((((((((((((((((((((`  ;
            export const ok = a.length + b.length + c.length;
        ";

        var js = ts.Transpile(src);

        Assert.Contains("ok", js);
    }

    [Fact]
    public void Transpile_MaxParseDepthConfigurable()
    {
        // Cap set to 10, ternary depth 12 → must throw (proves the property
        // actually drives the guard, not the default).
        var ts = CreateTranspiler(maxParseDepth: 10);

        Assert.Throws<TsTranspileException>(() =>
            ts.Transpile(BuildNestedTernary(12)));
    }
}
