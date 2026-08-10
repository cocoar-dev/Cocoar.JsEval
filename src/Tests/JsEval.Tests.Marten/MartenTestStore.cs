using JasperFx;
using Marten;
using Npgsql;

namespace JsEval.Tests.Marten;

/// <summary>
/// Backing store for the integration tests. These need a real PostgreSQL — the
/// point of the suite is that a sandboxed rule reaches an actual LINQ provider
/// and produces the same SQL a C# lambda would, which an in-memory
/// <c>IQueryable</c> cannot demonstrate.
///
/// When no database answers, the tests skip rather than fail: the rest of the
/// solution's tests must stay runnable without infrastructure.
/// </summary>
internal static class MartenTestStore
{
    /// <summary>
    /// Port 5433 is the development database; override the whole string with
    /// <c>JSEVAL_PG_CONNSTRING</c> when the local setup differs. The password is
    /// deliberately not a real one — a working local run supplies it through the
    /// environment variable rather than through this file.
    /// </summary>
    public static string ConnString { get; } =
        Environment.GetEnvironmentVariable("JSEVAL_PG_CONNSTRING") is { Length: > 0 } fromEnv
            ? fromEnv
            : "Host=localhost;Port=5433;Username=postgres;Password=postgres;Database=postgres";

    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(ConnString) { Timeout = 3 };
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    });

    /// <summary>Gate for <c>[Fact(SkipUnless = ...)]</c>.</summary>
    public static bool IsAvailable => Available.Value;

    public static DocumentStore Create() => DocumentStore.For(opts =>
    {
        opts.Connection(ConnString);
        // Separate from the experiment's jseval_sandbox schema so a test run and
        // a demo run cannot disturb each other's data.
        opts.DatabaseSchemaName = "jseval_tests";
        opts.AutoCreateSchemaObjects = AutoCreate.All;
    });

    public static async Task<DocumentStore> CreateSeeded()
    {
        var store = Create();
        await using var session = store.LightweightSession();
        session.DeleteWhere<User>(_ => true);
        session.Store(
            new User { Id = Guid.NewGuid(), Name = "Alice", Department = "eng",   Age = 34, IsActive = true },
            new User { Id = Guid.NewGuid(), Name = "Bob",   Department = "eng",   Age = 17, IsActive = true },
            new User { Id = Guid.NewGuid(), Name = "Cara",  Department = "sales", Age = 41, IsActive = true },
            new User { Id = Guid.NewGuid(), Name = "Dan",   Department = "eng",   Age = 50, IsActive = false });
        await session.SaveChangesAsync();
        return store;
    }
}

/// <summary>The persistence entity — carries a field no rule may ever see.</summary>
public sealed class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public int Age { get; set; }
    public bool IsActive { get; set; }
    public string PasswordHash { get; set; } = "top-secret";
}

/// <summary>What a rule's caller actually gets back.</summary>
public sealed class UserDto
{
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
}
