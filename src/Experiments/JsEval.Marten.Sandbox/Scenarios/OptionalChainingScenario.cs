using System.Linq.Expressions;
using Cocoar.JsEval.Linq;
using Jint;
using Marten;

namespace Cocoar.JsEval.Marten.Sandbox.Scenarios;

// Models that mirror the real authorization-project shapes that tripped over
// v3.1.0. `Author.Person` is nullable (an Author can be a company without a
// person), and `Person.Firstname` is nullable (a person may have only a legal
// identifier). Optional chaining on both hops is the natural authoring style.
public sealed class Author
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";    // "Person" | "Company"
    public bool IsActive { get; set; }
    public Person? Person { get; set; }
    public Company? Company { get; set; }
}

public sealed class Person
{
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }
}

public sealed class Company
{
    public string Name { get; set; } = "";
}

public sealed class Todo
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public Customer? Customer { get; set; }
}

public sealed class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

internal static class OptionalChainingScenario
{
    // Fixed Customer IDs — mirrors the demo-seed.json `customers.*` pattern.
    public static readonly Guid AcmeId    = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AlpineId  = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid CentralId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid BauerId   = new("44444444-4444-4444-4444-444444444444");

    public static async Task Seed(IDocumentStore store)
    {
        await using var session = store.LightweightSession();
        session.DeleteWhere<Author>(_ => true);
        session.DeleteWhere<Todo>(_ => true);

        // Authors — mix of Person / Company, with and without nested data, to
        // exercise every null-path in the predicate.
        session.Store(
            new Author { Id = Guid.NewGuid(), Type = "Person",  IsActive = true,
                Person  = new Person { Firstname = "Alice",   Lastname = "Albers"    } },
            new Author { Id = Guid.NewGuid(), Type = "Person",  IsActive = true,
                Person  = new Person { Firstname = "Leon",    Lastname = "Langer"    } },
            new Author { Id = Guid.NewGuid(), Type = "Person",  IsActive = true,
                Person  = new Person { Firstname = "Peter",   Lastname = "Paulsen"   } },
            new Author { Id = Guid.NewGuid(), Type = "Person",  IsActive = true,
                Person  = new Person { Firstname = "Markus",  Lastname = "Meier"     } },
            new Author { Id = Guid.NewGuid(), Type = "Person",  IsActive = false,
                Person  = new Person { Firstname = "Anna",    Lastname = "Amsel"     } },
            new Author { Id = Guid.NewGuid(), Type = "Person",  IsActive = true,
                Person  = new Person { Firstname = null,      Lastname = "Anonymous" } }, // Firstname null → chain short-circuits
            new Author { Id = Guid.NewGuid(), Type = "Person",  IsActive = true,
                Person  = null },                                                         // Person null → chain short-circuits earlier
            new Author { Id = Guid.NewGuid(), Type = "Company", IsActive = true,
                Company = new Company { Name = "ACME GmbH" } }                            // Type mismatch → excluded by `Type === 'Person'`
        );

        // Todos with customers from different sets (some with Customer=null).
        session.Store(
            new Todo { Id = Guid.NewGuid(), Title = "Deploy ACME",    Customer = new Customer { Id = AcmeId,    Name = "ACME" } },
            new Todo { Id = Guid.NewGuid(), Title = "Review Alpine",  Customer = new Customer { Id = AlpineId,  Name = "Alpine" } },
            new Todo { Id = Guid.NewGuid(), Title = "Migrate Central",Customer = new Customer { Id = CentralId, Name = "Central" } },
            new Todo { Id = Guid.NewGuid(), Title = "Audit Bauer",    Customer = new Customer { Id = BauerId,   Name = "Bauer" } },
            new Todo { Id = Guid.NewGuid(), Title = "Internal task",  Customer = null }                                           // no customer → excluded
        );

        await session.SaveChangesAsync();
        Console.WriteLine("Seeded 8 authors + 5 todos.");
    }

    public static async Task Run(IDocumentSession session)
    {
        await RunAuthorsPattern(session);
        Console.WriteLine();
        await RunTodosPattern(session);
    }

    /// <summary>
    /// The exact membership script rolled back in the demo-seed. Two-hop
    /// optional chain (`p.Person?.Firstname?.startsWith(...)`) combined in a 3-way
    /// OR with logical AND filters — the v3.1.0 shape that crashed Marten.
    /// </summary>
    private static async Task RunAuthorsPattern(IDocumentSession session)
    {
        Console.WriteLine("--- AUTHORS: p.Person?.Firstname?.startsWith('A'|'L'|'P') ---");

        // Three script variants, all semantically equivalent:
        //   1) v3.1.0 syntax with explicit `=== true` (failed on Marten pre-3.1.1)
        //   2) v3.1.1 natural syntax — no `=== true` needed
        //   3) Hand-rolled null checks (the workaround the user had to roll back to)
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

                var query = session.Query<Author>().Where(expr);
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

    /// <summary>
    /// The customer-scoped access-filter shape — single optional hop
    /// (`t.Customer?.Id`), compared with a typed Guid literal via `linq.guid(...)`,
    /// OR-chained across a whitelist of IDs. Also in the demo-seed rollback.
    /// </summary>
    private static async Task RunTodosPattern(IDocumentSession session)
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

                var query = session.Query<Todo>().Where(expr);
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
        try { return q.ToCommand().CommandText; }
        catch (Exception ex) { return $"(SQL capture failed: {ex.GetType().Name})"; }
    }

    private static string FirstLine(string s)
    {
        var idx = s.IndexOf('\n');
        return idx < 0 ? s : s[..idx] + " …";
    }
}
