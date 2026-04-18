using Cocoar.JsEval.Linq;
using Microsoft.EntityFrameworkCore;
using Jint;

namespace Cocoar.JsEval.EfCore.Sandbox.Scenarios;

/// <summary>
/// Reproduces the two real-world scripts that had to be rolled back to hand-rolled
/// null checks in a downstream authorization project under v3.1.0. Each script runs
/// in three variants (natural v3.1.1 syntax, v3.1.0 `=== true` style, hand-rolled
/// workaround) to verify all three translate to equivalent, EF-Core-executable SQL.
/// </summary>
internal static class OptionalChainingScenario
{
    public static readonly Guid AcmeId    = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AlpineId  = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid CentralId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid BauerId   = new("44444444-4444-4444-4444-444444444444");

    public static async Task Seed(AppDb db)
    {
        // Persons first (Authors reference them via FK).
        var pAlice  = new Person { Firstname = "Alice",   Lastname = "Albers"    };
        var pLeon   = new Person { Firstname = "Leon",    Lastname = "Langer"    };
        var pPeter  = new Person { Firstname = "Peter",   Lastname = "Paulsen"   };
        var pMarkus = new Person { Firstname = "Markus",  Lastname = "Meier"     };
        var pAnna   = new Person { Firstname = "Anna",    Lastname = "Amsel"     };
        var pAnon   = new Person { Firstname = null,      Lastname = "Anonymous" };
        db.Persons.AddRange(pAlice, pLeon, pPeter, pMarkus, pAnna, pAnon);
        await db.SaveChangesAsync();

        db.Authors.AddRange(
            new Author { Type = "Person",  IsActive = true,  PersonId = pAlice.Id  },
            new Author { Type = "Person",  IsActive = true,  PersonId = pLeon.Id   },
            new Author { Type = "Person",  IsActive = true,  PersonId = pPeter.Id  },
            new Author { Type = "Person",  IsActive = true,  PersonId = pMarkus.Id },
            new Author { Type = "Person",  IsActive = false, PersonId = pAnna.Id   },
            new Author { Type = "Person",  IsActive = true,  PersonId = pAnon.Id   }, // Firstname null
            new Author { Type = "Person",  IsActive = true,  PersonId = null       }, // Person null
            new Author { Type = "Company", IsActive = true,  PersonId = null       }
        );

        db.Customers.AddRange(
            new Customer { Id = AcmeId,    Name = "ACME"    },
            new Customer { Id = AlpineId,  Name = "Alpine"  },
            new Customer { Id = CentralId, Name = "Central" },
            new Customer { Id = BauerId,   Name = "Bauer"   }
        );
        await db.SaveChangesAsync();

        db.Todos.AddRange(
            new Todo { Title = "Deploy ACME",     CustomerId = AcmeId    },
            new Todo { Title = "Review Alpine",   CustomerId = AlpineId  },
            new Todo { Title = "Migrate Central", CustomerId = CentralId },
            new Todo { Title = "Audit Bauer",     CustomerId = BauerId   },
            new Todo { Title = "Internal task",   CustomerId = null      }
        );
        await db.SaveChangesAsync();

        Console.WriteLine("Seeded 8 authors + 5 todos.");
    }

    public static async Task Run(AppDb db)
    {
        await RunAuthorsPattern(db);
        Console.WriteLine();
        await RunTodosPattern(db);
    }

    private static async Task RunAuthorsPattern(AppDb db)
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

                var query = db.Authors.AsNoTracking().Include(a => a.Person).Where(expr);
                Console.WriteLine($"    SQL shape : {FirstLine(SafeSql(query))}");

                var results = await query.ToListAsync();
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

    private static async Task RunTodosPattern(AppDb db)
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

                var query = db.Todos.AsNoTracking().Include(t => t.Customer).Where(expr);
                Console.WriteLine($"    SQL shape : {FirstLine(SafeSql(query))}");

                var results = await query.ToListAsync();
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
        try { return q.ToQueryString(); }
        catch (Exception ex) { return $"(SQL capture failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]})"; }
    }

    private static string FirstLine(string s)
    {
        var nl = s.IndexOf('\n');
        return nl < 0 ? s : s[..nl] + " …";
    }
}
