using System.Linq.Expressions;
using Cocoar.JsEval.Linq;
using Cocoar.JsEval.Linq.Dependencies;
using Cocoar.JsEval.Marten.Sandbox;
using Cocoar.JsEval.Marten.Sandbox.Scenarios;
using Jint;
using Jint.Native;
using Marten;

// Npgsql 6+ rejects DateTime with Kind=Utc by default; re-enable the legacy
// behaviour so the demo can seed UtcNow-based data without switching to
// DateTimeOffset/NodaTime. Irrelevant to the translator itself.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

Console.WriteLine("=== Cocoar.JsEval.Linq -- Marten Integration Demo ===");
Console.WriteLine();

var store = SandboxStore.Create();
await SandboxStore.Seed(store);

await Run("1. JS -> Expression -> Marten: SQL byte-identical to C# source lambda", async session =>
{
    // Baseline: hand-written C# — what Marten SHOULD produce.
    Expression<Func<User, bool>> csBaseline = u => u.Name.StartsWith("A") && u.IsActive;
    var baselineSql = session.Query<User>().Where(csBaseline).ToCommand().CommandText;

    // Engine exposes the bare IMartenQueryable<User>. No wrapper, no strings.
    var engine = new Engine(opts =>
    {
        opts.AllowClr(typeof(User).Assembly);
        opts.AddExtensionMethods(typeof(JsLinqExtensions));
    });
    engine.SetValue("users", session.Query<User>());

    using (JsLinqContext.Scope(engine))
    {
        var result = engine.Evaluate("users.where(u => u.Name.startsWith('A') && u.IsActive)");
        var filtered = (IQueryable<User>)result.ToObject()!;

        var actual = filtered.ToCommand().CommandText;
        Console.WriteLine($"SQL:\n  {actual}");
        var rows = await filtered.ToListAsync();
        Console.WriteLine($"Rows: {rows.Count} -> {string.Join(", ", rows.Select(u => u.Name))}");
        Console.WriteLine();
        Console.WriteLine(baselineSql == actual
            ? ">>> BYTE-IDENTICAL TO C# BASELINE."
            : $">>> DIFFERS!\nBaseline: {baselineSql}\nActual:   {actual}");

        // The synchronous terminals execute the query on the spot, which Marten 9
        // refuses. Their async counterparts find Marten's own CountAsync /
        // FirstOrDefaultAsync at run time, so a script awaits them exactly as C#
        // would. Both are shown side by side.
        foreach (var (label, js) in new[]
                 {
                     ("users.count(u => u.IsActive)      [sync] ", "users.count(u => u.IsActive)"),
                     ("users.find(u => u.Name === 'Bob') [sync] ", "users.find(u => u.Name === 'Bob')"),
                 })
        {
            try { Console.WriteLine($"\n{label} -> {engine.Evaluate(js).ToObject()}"); }
            catch (Exception ex) { Console.WriteLine($"\n{label} -> REFUSED: {ex.Message.Split('\n')[0]}"); }
        }

        var active = await JsLinqExtensions.CountAsync(session.Query<User>(), JsValue.Null);
        Console.WriteLine($"\nusers.countAsync(null)             [async] -> {active}");

        var bob = await JsLinqExtensions.FindAsync(
            session.Query<User>().Where(u => u.Name == "Bob"), JsValue.Null);
        Console.WriteLine($"users.findAsync(...)               [async] -> {bob?.Name} (age={bob?.Age})");
    }
});

await Run("2. CsDateTime fluent API in predicate: cutoff.AddDays(-7) from JS", async session =>
{
    var cutoffValue = DateTime.UtcNow;

    // Baseline: hand-written C# with plain DateTime arithmetic.
    Expression<Func<User, bool>> csPred = u => u.CreatedAt > cutoffValue.AddDays(-7);
    var baseline = session.Query<User>().Where(csPred).ToCommand().CommandText;

    var engine = new Engine(opts =>
    {
        opts.AllowClr(typeof(User).Assembly);
        opts.AddExtensionMethods(typeof(JsLinqExtensions));
    });
    Cocoar.JsEval.Engine.CsDateTimeGlobals.Register(engine);
    engine.SetValue("users", session.Query<User>());
    engine.SetValue("cutoff", new Cocoar.JsEval.Engine.CsDateTime(cutoffValue));

    using (JsLinqContext.Scope(engine))
    {
        var result = engine.Evaluate("users.where(u => u.CreatedAt > cutoff.AddDays(-7))");
        var filtered = (IQueryable<User>)result.ToObject()!;
        var actual = filtered.ToCommand().CommandText;

        Console.WriteLine($"SQL:\n  {actual}");
        var rows = await filtered.ToListAsync();
        Console.WriteLine($"Rows: {rows.Count} -> {string.Join(", ", rows.Select(u => u.Name))}");
        Console.WriteLine();
        Console.WriteLine(baseline == actual
            ? ">>> BYTE-IDENTICAL TO C# BASELINE (implicit-op CsDateTime → DateTime + AddDays)."
            : $">>> DIFFERS!\nBaseline: {baseline}\nActual:   {actual}");
    }
});

await Run("3. OrderBy / ThenByDescending from JS: byte-identical SQL", async session =>
{
    Expression<Func<User, bool>> csPred = u => u.IsActive;
    var baseline = session.Query<User>()
        .Where(csPred)
        .OrderBy(u => u.Name)
        .ThenByDescending(u => u.Age)
        .ToCommand().CommandText;

    var engine = new Engine(opts =>
    {
        opts.AllowClr(typeof(User).Assembly);
        opts.AddExtensionMethods(typeof(JsLinqExtensions));
    });
    engine.SetValue("users", session.Query<User>());

    using (JsLinqContext.Scope(engine))
    {
        var result = engine.Evaluate(@"
            users.where(u => u.IsActive)
                 .orderBy(u => u.Name)
                 .thenByDescending(u => u.Age)
        ");
        var ordered = (IQueryable<User>)result.ToObject()!;
        var actual = ordered.ToCommand().CommandText;
        Console.WriteLine($"SQL:\n  {actual}");
        var rows = await ordered.ToListAsync();
        Console.WriteLine($"Rows: {rows.Count} -> {string.Join(", ", rows.Select(u => $"{u.Name}({u.Age})"))}");
        Console.WriteLine();
        Console.WriteLine(baseline == actual
            ? ">>> BYTE-IDENTICAL TO C# BASELINE."
            : $">>> DIFFERS!\nBaseline: {baseline}\nActual:   {actual}");
    }
});

await Run("4. Method matrix — which string/collection methods does Marten translate?", async session =>
{
    var tests = new (string Name, Expression<Func<User, bool>> Expr)[]
    {
        ("StartsWith",        u => u.Name.StartsWith("A")),
        ("EndsWith",          u => u.Name.EndsWith("e")),
        ("Contains",          u => u.Name.Contains("o")),
        ("IndexOf >= 0",      u => u.Name.IndexOf("A") >= 0),
        ("ToLower",           u => u.Name.ToLower() == "alice"),
        ("ToUpper",           u => u.Name.ToUpper() == "ALICE"),
        ("int.ToString",      u => u.Age.ToString() == "30"),
        ("Tags.Any(== vip)",  u => u.Tags.Any(t => t == "vip")),
        ("Tags.Contains vip", u => u.Tags.Contains("vip")),
    };

    foreach (var (name, expr) in tests)
    {
        try
        {
            var _ = await session.Query<User>().Where(expr).ToListAsync();
            Console.WriteLine($"  {name,-22} ✓");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {name,-22} ✗  {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }
});

await Run("5. Dependency tracking: which properties does the script touch?", async session =>
{
    var engine = new Engine(opts =>
    {
        opts.AllowClr(typeof(User).Assembly);
        opts.AddExtensionMethods(typeof(JsLinqExtensions));
    });
    engine.SetValue("users", session.Query<User>());

    using (JsLinqContext.Scope(engine))
    {
        var jsFn = engine.Evaluate(@"
            (u) => u.Name.startsWith('A')
                && u.IsActive
                && u.Address.City === 'Vienna'
                && u.Tags.some(t => t === 'vip')
        ");
        var expr = JsExpressionTranslator.Translate<User, bool>(jsFn, engine);
        Console.WriteLine($"Expression: {expr}");

        var deps = ExpressionDependencyCollector.Collect(expr);
        Console.WriteLine($"\nPaths:      {deps}");
        Console.WriteLine($"Top-level:  {string.Join(", ", deps.TopLevel)}");

        Console.WriteLine("\nReactive re-run matrix (user update -> should we re-run this query?):");
        foreach (var prop in new[] { "Name", "Email", "IsActive", "Address.City", "Address.Zip", "Tags", "Age" })
            Console.WriteLine($"  change '{prop,-14}' -> re-run? {(deps.DependsOn(prop) ? "YES" : "no ")}");
    }
});

await OptionalChainingScenario.Seed(store);
await Run("6. Optional chaining against Marten (v3.1.0 regression case)", OptionalChainingScenario.Run);

await DiscriminatorMappingScenario.Seed(store);
await Run("7. Discriminator mapping: Type.Is / Type.IsOneOf + AND-narrowing", DiscriminatorMappingScenario.Run);

Console.WriteLine();
Console.WriteLine("=== DONE ===");

async Task Run(string title, Func<IDocumentSession, Task> action)
{
    Console.WriteLine(new string('=', 70));
    Console.WriteLine($"SCENARIO: {title}");
    Console.WriteLine(new string('-', 70));
    try
    {
        await using var session = store.LightweightSession();
        await action(session);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    }
    Console.WriteLine();
}
