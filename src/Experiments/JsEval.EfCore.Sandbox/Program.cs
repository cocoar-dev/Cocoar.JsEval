using System.Linq.Expressions;
using Cocoar.JsEval.EfCore.Sandbox;
using Cocoar.JsEval.Linq;
using Cocoar.JsEval.Linq.Dependencies;
using Jint;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

Console.WriteLine("=== Cocoar.JsEval.Linq -- EF Core + SQLite Integration Demo ===");
Console.WriteLine();

// Keep the SQLite in-memory connection open for the lifetime of the demo.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var options = new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options;
await using (var setup = new AppDb(options))
{
    await setup.Database.EnsureCreatedAsync();
    setup.Users.AddRange(
        new User { Name = "Alice",   Email = "a@x.com",  IsActive = true,  Age = 30, City = "Vienna", Zip = "1010" },
        new User { Name = "Andrew",  Email = "an@x.com", IsActive = false, Age = 42, City = "Graz",   Zip = "8010" },
        new User { Name = "Bob",     Email = "b@x.com",  IsActive = true,  Age = 25, City = "Vienna", Zip = "1020" },
        new User { Name = "Charlie", Email = "c@x.com",  IsActive = true,  Age = 50, City = "Linz",   Zip = "4020" }
    );
    await setup.SaveChangesAsync();
}

await Run("1. JS -> Expression -> EF Core SQLite: byte-identical SQL", async db =>
{
    Expression<Func<User, bool>> csBaseline = u => u.Name.StartsWith("A") && u.IsActive;
    var baselineSql = db.Users.Where(csBaseline).ToQueryString();

    var engine = new Engine(opts =>
    {
        opts.AllowClr(typeof(User).Assembly);
        opts.AddExtensionMethods(typeof(JsLinqExtensions));
    });
    // Expose as IQueryable<T> (not DbSet<T>) so JS doesn't see EF's instance
    // methods like .Find(object[] keys) that collide with our extensions.
    engine.SetValue("users", db.Users.AsNoTracking());

    using (JsLinqContext.Scope(engine))
    {
        var result = engine.Evaluate("users.where(u => u.Name.startsWith('A') && u.IsActive)");
        var filtered = (IQueryable<User>)result.ToObject()!;
        var actualSql = filtered.ToQueryString();

        Console.WriteLine("Generated SQL:");
        Console.WriteLine(actualSql);
        var rows = await filtered.ToListAsync();
        Console.WriteLine($"\nRows: {rows.Count} -> {string.Join(", ", rows.Select(u => u.Name))}");
        Console.WriteLine();
        Console.WriteLine(baselineSql == actualSql
            ? ">>> BYTE-IDENTICAL TO C# BASELINE."
            : $">>> DIFFERS!\nBaseline:\n{baselineSql}\nActual:\n{actualSql}");

        var count = engine.Evaluate("users.count(u => u.IsActive)").AsNumber();
        Console.WriteLine($"\nusers.count(u => u.IsActive) -> {count}");

        var bob = (User?)engine.Evaluate("users.find(u => u.Name === 'Bob')").ToObject();
        Console.WriteLine($"users.find(u => u.Name === 'Bob') -> {bob?.Name} (age={bob?.Age})");
    }
});

await Run("2. Dependency tracking: reactive re-run matrix", async db =>
{
    var engine = new Engine(opts =>
    {
        opts.AllowClr(typeof(User).Assembly);
        opts.AddExtensionMethods(typeof(JsLinqExtensions));
    });
    // Expose as IQueryable<T> (not DbSet<T>) so JS doesn't see EF's instance
    // methods like .Find(object[] keys) that collide with our extensions.
    engine.SetValue("users", db.Users.AsNoTracking());

    using (JsLinqContext.Scope(engine))
    {
        var jsFn = engine.Evaluate(@"
            (u) => u.Name.startsWith('A')
                && u.IsActive
                && u.City === 'Vienna'
        ");
        var expr = JsExpressionTranslator.Translate<User, bool>(jsFn, engine);
        Console.WriteLine($"Expression: {expr}");

        var deps = ExpressionDependencyCollector.Collect(expr);
        Console.WriteLine($"\nPaths:     {deps}");
        Console.WriteLine($"Top-level: {string.Join(", ", deps.TopLevel)}");
        Console.WriteLine();
        Console.WriteLine("Reactive re-run matrix:");
        foreach (var prop in new[] { "Name", "Email", "IsActive", "City", "Zip", "Age" })
            Console.WriteLine($"  change '{prop,-8}' -> re-run? {(deps.DependsOn(prop) ? "YES" : "no ")}");
    }
    await Task.CompletedTask;
});

Console.WriteLine();
Console.WriteLine("=== DONE ===");

async Task Run(string title, Func<AppDb, Task> action)
{
    Console.WriteLine(new string('=', 70));
    Console.WriteLine($"SCENARIO: {title}");
    Console.WriteLine(new string('-', 70));
    try
    {
        await using var db = new AppDb(options);
        await action(db);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    }
    Console.WriteLine();
}
