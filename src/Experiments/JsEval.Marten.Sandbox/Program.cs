using System.Linq.Expressions;
using Cocoar.JsEval.Linq;
using Cocoar.JsEval.Linq.Dependencies;
using Cocoar.JsEval.Marten.Sandbox;
using Jint;
using Marten;

// Npgsql 6+ rejects DateTime with Kind=Utc by default; re-enable the legacy
// behaviour so the demo can seed UtcNow-based data without switching to
// DateTimeOffset/NodaTime. Irrelevant to the translator itself.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

Console.WriteLine("=== Cocoar.JsEval.Linq -- Marten Integration Demo ===");
Console.WriteLine();

var store = SandboxStore.Create();
await SandboxStore.Seed(store);

await Run("1. JS -> Expression -> Marten: SQL byte-identical to C# source lambda", session =>
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
        var rows = filtered.ToList();
        Console.WriteLine($"Rows: {rows.Count} -> {string.Join(", ", rows.Select(u => u.Name))}");
        Console.WriteLine();
        Console.WriteLine(baselineSql == actual
            ? ">>> BYTE-IDENTICAL TO C# BASELINE."
            : $">>> DIFFERS!\nBaseline: {baselineSql}\nActual:   {actual}");

        var count = engine.Evaluate("users.count(u => u.IsActive)").AsNumber();
        Console.WriteLine($"\nusers.count(u => u.IsActive) -> {count}");

        var bob = (User?)engine.Evaluate("users.find(u => u.Name === 'Bob')").ToObject();
        Console.WriteLine($"users.find(u => u.Name === 'Bob') -> {bob?.Name} (age={bob?.Age})");
    }
});

await Run("2. CsDateTime fluent API in predicate: cutoff.AddDays(-7) from JS", session =>
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
        var rows = filtered.ToList();
        Console.WriteLine($"Rows: {rows.Count} -> {string.Join(", ", rows.Select(u => u.Name))}");
        Console.WriteLine();
        Console.WriteLine(baseline == actual
            ? ">>> BYTE-IDENTICAL TO C# BASELINE (implicit-op CsDateTime → DateTime + AddDays)."
            : $">>> DIFFERS!\nBaseline: {baseline}\nActual:   {actual}");
    }
});

await Run("3. OrderBy / ThenByDescending from JS: byte-identical SQL", session =>
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
        var rows = ordered.ToList();
        Console.WriteLine($"Rows: {rows.Count} -> {string.Join(", ", rows.Select(u => $"{u.Name}({u.Age})"))}");
        Console.WriteLine();
        Console.WriteLine(baseline == actual
            ? ">>> BYTE-IDENTICAL TO C# BASELINE."
            : $">>> DIFFERS!\nBaseline: {baseline}\nActual:   {actual}");
    }
});

await Run("4. Dependency tracking: which properties does the script touch?", session =>
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

Console.WriteLine();
Console.WriteLine("=== DONE ===");

async Task Run(string title, Action<IDocumentSession> action)
{
    Console.WriteLine(new string('=', 70));
    Console.WriteLine($"SCENARIO: {title}");
    Console.WriteLine(new string('-', 70));
    try
    {
        await using var session = store.LightweightSession();
        action(session);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    }
    Console.WriteLine();
}
