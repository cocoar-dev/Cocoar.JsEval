using System.Linq.Expressions;
using Cocoar.JsEval.Linq;
using Cocoar.JsEval.Linq.Dependencies;
using Cocoar.JsEval.Linq2Db.Sandbox;
using Jint;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;

Console.WriteLine("=== Cocoar.JsEval.Linq -- LINQ2DB + SQLite Integration Demo ===");
Console.WriteLine();

const string ConnStr = "Data Source=:memory:;Mode=Memory;Cache=Shared";

// A single persistent connection keeps the in-memory DB alive for the whole demo.
await using var keepalive = new Microsoft.Data.Sqlite.SqliteConnection(ConnStr);
await keepalive.OpenAsync();

DataOptions dataOptions = new DataOptions()
    .UseConnectionString(ProviderName.SQLiteMS, ConnStr);

using (var setup = new DataConnection(dataOptions))
{
    setup.Execute(@"CREATE TABLE Users (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Name TEXT NOT NULL,
        Email TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        Age INTEGER NOT NULL,
        City TEXT NOT NULL,
        Zip TEXT NOT NULL
    )");
    setup.GetTable<User>().BulkCopy(
    [
        new User { Name = "Alice",   Email = "a@x.com",  IsActive = true,  Age = 30, City = "Vienna", Zip = "1010" },
        new User { Name = "Andrew",  Email = "an@x.com", IsActive = false, Age = 42, City = "Graz",   Zip = "8010" },
        new User { Name = "Bob",     Email = "b@x.com",  IsActive = true,  Age = 25, City = "Vienna", Zip = "1020" },
        new User { Name = "Charlie", Email = "c@x.com",  IsActive = true,  Age = 50, City = "Linz",   Zip = "4020" },
    ]);
}

await Run("1. JS -> Expression -> LINQ2DB SQLite: byte-identical SQL", db =>
{
    Expression<Func<User, bool>> csBaseline = u => u.Name.StartsWith("A") && u.IsActive;
    var baselineSql = db.GetTable<User>().Where(csBaseline).ToSqlQuery().Sql;

    var engine = new Engine(opts =>
    {
        opts.AllowClr(typeof(User).Assembly);
        opts.AddExtensionMethods(typeof(JsLinqExtensions));
    });
    engine.SetValue("users", db.GetTable<User>().AsQueryable());

    using (JsLinqContext.Scope(engine))
    {
        var result = engine.Evaluate("users.where(u => u.Name.startsWith('A') && u.IsActive)");
        var filtered = (IQueryable<User>)result.ToObject()!;
        var actualSql = filtered.ToSqlQuery().Sql;

        Console.WriteLine("Generated SQL:");
        Console.WriteLine(actualSql);
        var rows = filtered.ToList();
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

await Run("2. Dependency tracking: reactive re-run matrix", db =>
{
    var engine = new Engine(opts =>
    {
        opts.AllowClr(typeof(User).Assembly);
        opts.AddExtensionMethods(typeof(JsLinqExtensions));
    });
    engine.SetValue("users", db.GetTable<User>().AsQueryable());

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
});

Console.WriteLine();
Console.WriteLine("=== DONE ===");

async Task Run(string title, Action<DataConnection> action)
{
    Console.WriteLine(new string('=', 70));
    Console.WriteLine($"SCENARIO: {title}");
    Console.WriteLine(new string('-', 70));
    try
    {
        using var db = new DataConnection(dataOptions);
        action(db);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    }
    Console.WriteLine();
    await Task.CompletedTask;
}
