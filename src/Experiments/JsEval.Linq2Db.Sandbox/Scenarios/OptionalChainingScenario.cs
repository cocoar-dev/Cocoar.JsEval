using Cocoar.JsEval.Linq;
using Jint;
using LinqToDB;
using LinqToDB.Data;

namespace Cocoar.JsEval.Linq2Db.Sandbox.Scenarios;

/// <summary>
/// LINQ2DB variant of the same optional-chaining regression scenario used in the
/// Marten and EF Core sandboxes. Three script variants per pattern (natural v3.1.1,
/// v3.1.0 `=== true` style, hand-rolled workaround) executed against the same
/// seeded dataset, verifying the translator emits shapes LINQ2DB translates.
/// </summary>
internal static class OptionalChainingScenario
{
    public static readonly Guid AcmeId    = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AlpineId  = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid CentralId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid BauerId   = new("44444444-4444-4444-4444-444444444444");

    public static void Seed(DataConnection db)
    {
        db.Execute(@"CREATE TABLE Persons (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Firstname TEXT NULL,
            Lastname TEXT NULL
        )");
        db.Execute(@"CREATE TABLE Authors (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Type TEXT NOT NULL,
            IsActive INTEGER NOT NULL,
            PersonId INTEGER NULL
        )");
        db.Execute(@"CREATE TABLE Customers (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL
        )");
        db.Execute(@"CREATE TABLE Todos (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            CustomerId TEXT NULL
        )");

        db.GetTable<Person>().BulkCopy(
        [
            new Person { Firstname = "Alice",  Lastname = "Albers"    },
            new Person { Firstname = "Leon",   Lastname = "Langer"    },
            new Person { Firstname = "Peter",  Lastname = "Paulsen"   },
            new Person { Firstname = "Markus", Lastname = "Meier"     },
            new Person { Firstname = "Anna",   Lastname = "Amsel"     },
            new Person { Firstname = null,     Lastname = "Anonymous" },
        ]);

        // PersonIds are 1..6 in insertion order.
        db.GetTable<Author>().BulkCopy(
        [
            new Author { Type = "Person",  IsActive = true,  PersonId = 1    },
            new Author { Type = "Person",  IsActive = true,  PersonId = 2    },
            new Author { Type = "Person",  IsActive = true,  PersonId = 3    },
            new Author { Type = "Person",  IsActive = true,  PersonId = 4    },
            new Author { Type = "Person",  IsActive = false, PersonId = 5    },
            new Author { Type = "Person",  IsActive = true,  PersonId = 6    }, // Firstname null
            new Author { Type = "Person",  IsActive = true,  PersonId = null },
            new Author { Type = "Company", IsActive = true,  PersonId = null },
        ]);

        db.GetTable<Customer>().BulkCopy(
        [
            new Customer { Id = AcmeId,    Name = "ACME"    },
            new Customer { Id = AlpineId,  Name = "Alpine"  },
            new Customer { Id = CentralId, Name = "Central" },
            new Customer { Id = BauerId,   Name = "Bauer"   },
        ]);

        db.GetTable<Todo>().BulkCopy(
        [
            new Todo { Title = "Deploy ACME",     CustomerId = AcmeId    },
            new Todo { Title = "Review Alpine",   CustomerId = AlpineId  },
            new Todo { Title = "Migrate Central", CustomerId = CentralId },
            new Todo { Title = "Audit Bauer",     CustomerId = BauerId   },
            new Todo { Title = "Internal task",   CustomerId = null      },
        ]);

        Console.WriteLine("Seeded 8 authors + 5 todos.");
    }

    public static void Run(DataConnection db)
    {
        RunAuthorsPattern(db);
        Console.WriteLine();
        RunTodosPattern(db);
    }

    private static void RunAuthorsPattern(DataConnection db)
    {
        Console.WriteLine("--- AUTHORS: p.Person?.Firstname?.startsWith('A'|'L'|'P') ---");

        var scripts = new (string Label, string Js)[]
        {
            ("Natural (v3.1.1)",
                "(p) => p.Type === 'Person' && p.IsActive && (" +
                "  p.Person?.Firstname?.startsWith('A') || " +
                "  p.Person?.Firstname?.startsWith('L') || " +
                "  p.Person?.Firstname?.startsWith('P'))"),

            ("With === true (v3.1.0 style)",
                "(p) => p.Type === 'Person' && p.IsActive && (" +
                "  p.Person?.Firstname?.startsWith('A') === true || " +
                "  p.Person?.Firstname?.startsWith('L') === true || " +
                "  p.Person?.Firstname?.startsWith('P') === true)"),

            ("Hand-rolled null checks (workaround)",
                "(p) => p.Type === 'Person' && p.IsActive && " +
                "  p.Person != null && p.Person.Firstname != null && (" +
                "    p.Person.Firstname.startsWith('A') || " +
                "    p.Person.Firstname.startsWith('L') || " +
                "    p.Person.Firstname.startsWith('P'))"),
        };

        foreach (var (label, js) in scripts)
        {
            Console.WriteLine($"\n  [{label}]");
            try
            {
                var engine = new Jint.Engine(opts => opts.AllowClr(typeof(Author).Assembly));
                var jsFn = engine.Evaluate(js);
                var expr = JsExpressionTranslator.Translate<Author, bool>(jsFn, engine);
                Console.WriteLine($"    Expression: {expr.Body}");

                var query = db.GetTable<Author>().LoadWith(a => a.Person).Where(expr);
                Console.WriteLine($"    SQL shape : {FirstLine(SafeSql(query))}");

                var results = query.ToList();
                var names = string.Join(", ", results.Select(a => a.Person?.Firstname ?? "(null)"));
                Console.WriteLine($"    Results   : {results.Count} rows → {names}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        Console.WriteLine("\n  Expected: Alice, Leon, Peter (3 rows)");
    }

    private static void RunTodosPattern(DataConnection db)
    {
        Console.WriteLine("--- TODOS: t.Customer?.Id in (ACME|Alpine|Central) ---");

        var scripts = new (string Label, string Js)[]
        {
            ("Natural (v3.1.1)",
                $"(t) => t.Customer?.Id === linq.guid('{AcmeId}') || " +
                $"       t.Customer?.Id === linq.guid('{AlpineId}') || " +
                $"       t.Customer?.Id === linq.guid('{CentralId}')"),

            ("Hand-rolled null check (workaround)",
                $"(t) => t.Customer != null && (" +
                $"  t.Customer.Id === linq.guid('{AcmeId}') || " +
                $"  t.Customer.Id === linq.guid('{AlpineId}') || " +
                $"  t.Customer.Id === linq.guid('{CentralId}'))"),
        };

        foreach (var (label, js) in scripts)
        {
            Console.WriteLine($"\n  [{label}]");
            try
            {
                var engine = new Jint.Engine(opts => opts.AllowClr(typeof(Todo).Assembly));
                var jsFn = engine.Evaluate(js);
                var expr = JsExpressionTranslator.Translate<Todo, bool>(jsFn, engine);
                Console.WriteLine($"    Expression: {expr.Body}");

                var query = db.GetTable<Todo>().LoadWith(t => t.Customer).Where(expr);
                Console.WriteLine($"    SQL shape : {FirstLine(SafeSql(query))}");

                var results = query.ToList();
                var titles = string.Join(", ", results.Select(t => t.Title));
                Console.WriteLine($"    Results   : {results.Count} rows → {titles}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        Console.WriteLine("\n  Expected: Deploy ACME, Review Alpine, Migrate Central (3 rows)");
    }

    private static string SafeSql<T>(IQueryable<T> q)
    {
        try { return q.ToSqlQuery().Sql; }
        catch (Exception ex) { return $"(SQL capture failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]})"; }
    }

    private static string FirstLine(string s)
    {
        var nl = s.IndexOf('\n');
        return nl < 0 ? s : s[..nl] + " …";
    }
}
