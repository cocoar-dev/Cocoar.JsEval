using JasperFx;
using Marten;
using Npgsql;

namespace Cocoar.JsEval.Marten.Sandbox;

internal static class SandboxStore
{
    public const string ConnString = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";

    public static DocumentStore Create() => DocumentStore.For(opts =>
    {
        opts.Connection(ConnString);
        opts.DatabaseSchemaName = "jseval_sandbox";
        opts.AutoCreateSchemaObjects = AutoCreate.All;
    });

    public static async Task Seed(DocumentStore store)
    {
        await using var session = store.LightweightSession();
        session.DeleteWhere<User>(_ => true);
        session.Store(
            new User { Id = Guid.NewGuid(), Name = "Alice",   Email = "a@x.com",  IsActive = true,  Age = 30, CreatedAt = DateTime.UtcNow.AddDays(-10), Tags = ["vip", "early"], Address = new Address { City = "Vienna", Zip = "1010" } },
            new User { Id = Guid.NewGuid(), Name = "Andrew",  Email = "an@x.com", IsActive = false, Age = 42, CreatedAt = DateTime.UtcNow.AddDays(-45), Tags = ["new"],          Address = new Address { City = "Graz",   Zip = "8010" } },
            new User { Id = Guid.NewGuid(), Name = "Bob",     Email = "b@x.com",  IsActive = true,  Age = 25, CreatedAt = DateTime.UtcNow.AddDays(-3),  Tags = ["vip"],          Address = new Address { City = "Vienna", Zip = "1020" } },
            new User { Id = Guid.NewGuid(), Name = "Charlie", Email = "c@x.com",  IsActive = true,  Age = 50, CreatedAt = DateTime.UtcNow.AddDays(-90), Tags = [],               Address = new Address { City = "Linz",   Zip = "4020" } }
        );
        await session.SaveChangesAsync();
        Console.WriteLine("Seeded 4 users.");
        Console.WriteLine();
    }

    public static void PrintSql(IQueryable<User> query)
    {
        try
        {
            var cmd = query.ToCommand();
            Console.WriteLine("SQL:");
            Console.WriteLine(cmd.CommandText);
            if (cmd.Parameters.Count > 0)
            {
                Console.WriteLine("Parameters:");
                foreach (NpgsqlParameter p in cmd.Parameters)
                    Console.WriteLine($"  {p.ParameterName} = {p.Value}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"(SQL capture failed: {ex.GetType().Name}: {ex.Message})");
        }
    }

    public static void PrintResults(IQueryable<User> query)
    {
        try
        {
            var results = query.ToList();
            Console.WriteLine($"Results: {results.Count} row(s)");
            foreach (var u in results)
                Console.WriteLine($"  - {u.Name} (active={u.IsActive}, age={u.Age})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"(Execution failed: {ex.GetType().Name}: {ex.Message})");
        }
    }
}
