using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Marten;

namespace Cocoar.JsEval.Marten.Sandbox.Scenarios;

/// <summary>
/// Demonstrates property-based discriminator mapping with Marten.
/// Instead of CLR-type hierarchy (p is PersonParticipant), we map the "ParticipantType"
/// string property — Marten translates this to a simple JSONB field comparison.
/// </summary>
public static class DiscriminatorMappingScenario
{
    private static readonly List<DiscriminatorMapping> Mappings =
    [
        new(typeof(Participant), "person",  typeof(PersonView),  "ParticipantType"),
        new(typeof(Participant), "company", typeof(CompanyView), "ParticipantType"),
    ];

    private static readonly TranslationOptions TranslationOpts = new()
    {
        DiscriminatorMappings = Mappings
    };

    public static async Task Seed(DocumentStore store)
    {
        await using var session = store.LightweightSession();
        session.DeleteWhere<Participant>(_ => true);
        session.Store(
            new Participant { Id = Guid.NewGuid(), Name = "Alice Müller",  ParticipantType = "person",  Firstname = "Alice", Lastname = "Müller"  },
            new Participant { Id = Guid.NewGuid(), Name = "Bob Huber",     ParticipantType = "person",  Firstname = "Bob",   Lastname = "Huber"   },
            new Participant { Id = Guid.NewGuid(), Name = "Anna Schmidt",  ParticipantType = "person",  Firstname = "Anna",  Lastname = "Schmidt" },
            new Participant { Id = Guid.NewGuid(), Name = "Acme Corp",     ParticipantType = "company", VatNumber = "ATU1234" },
            new Participant { Id = Guid.NewGuid(), Name = "Widgets GmbH",  ParticipantType = "company", VatNumber = "ATU5678" }
        );
        await session.SaveChangesAsync();
        Console.WriteLine("Seeded 5 participants (property-based discrimination).");
        Console.WriteLine();
    }

    public static void Run(IDocumentSession session)
    {
        var engine = new Jint.Engine();
        engine.SetValue("Type", new JsTypeGlobal(Mappings, new Dictionary<string, Type>(), []));

        var person  = new Participant { Name = "X", ParticipantType = "person"  };
        var company = new Participant { Name = "Y", ParticipantType = "company" };
        engine.SetValue("person",  person);
        engine.SetValue("company", company);

        // ── 1. Runtime ───────────────────────────────────────────────────────
        Console.WriteLine("Runtime Type.Is / Type.IsOneOf:");
        Console.WriteLine($"  Type.Is(person,  'person')  -> {engine.Evaluate("Type.Is(person, 'person')")}");
        Console.WriteLine($"  Type.Is(company, 'person')  -> {engine.Evaluate("Type.Is(company, 'person')")}");
        Console.WriteLine($"  Type.IsOneOf(person,  ['person','company']) -> {engine.Evaluate("Type.IsOneOf(person,  ['person','company'])")}");
        Console.WriteLine($"  Type.IsOneOf(company, ['person','company']) -> {engine.Evaluate("Type.IsOneOf(company, ['person','company'])")}");

        // ── 2. LINQ: Type.Is → Marten SQL ───────────────────────────────────
        Console.WriteLine("\nLINQ Type.Is → Marten SQL:");
        var jsFn = engine.Evaluate("(p) => Type.Is(p, 'person')");
        var expr  = JsExpressionTranslator.Translate<Participant, bool>(jsFn, engine, TranslationOpts);
        Console.WriteLine($"  Expression : {expr}");
        SandboxStore.PrintSql(session.Query<Participant>().Where(expr));
        var persons = session.Query<Participant>().Where(expr).ToList();
        Console.WriteLine($"  Rows ({persons.Count}): {string.Join(", ", persons.Select(p => p.Name))}");

        // ── 3. LINQ: Type.IsOneOf → Marten SQL ──────────────────────────────
        Console.WriteLine("\nLINQ Type.IsOneOf → Marten SQL:");
        var jsFnOneOf = engine.Evaluate("(p) => Type.IsOneOf(p, ['person', 'company'])");
        var exprOneOf = JsExpressionTranslator.Translate<Participant, bool>(jsFnOneOf, engine, TranslationOpts);
        Console.WriteLine($"  Expression : {exprOneOf}");
        SandboxStore.PrintSql(session.Query<Participant>().Where(exprOneOf));
        var all = session.Query<Participant>().Where(exprOneOf).ToList();
        Console.WriteLine($"  Rows ({all.Count}): {string.Join(", ", all.Select(p => p.Name))}");

        // ── 4. LINQ: AND-narrowing ───────────────────────────────────────────
        Console.WriteLine("\nLINQ AND (Type.Is + property filter):");
        var jsFnAnd = engine.Evaluate("(p) => Type.Is(p, 'person') && p.Firstname.startsWith('A')");
        var exprAnd = JsExpressionTranslator.Translate<Participant, bool>(jsFnAnd, engine, TranslationOpts);
        Console.WriteLine($"  Expression : {exprAnd}");
        SandboxStore.PrintSql(session.Query<Participant>().Where(exprAnd));
        var filtered = session.Query<Participant>().Where(exprAnd).ToList();
        Console.WriteLine($"  Rows ({filtered.Count}): {string.Join(", ", filtered.Select(p => p.Name))}");

        // ── 5. LINQ: IsOneOf AND ─────────────────────────────────────────────
        Console.WriteLine("\nLINQ IsOneOf AND:");
        var jsFnOneOfAnd = engine.Evaluate("(p) => Type.IsOneOf(p, ['person']) && p.Firstname.startsWith('A')");
        var exprOneOfAnd = JsExpressionTranslator.Translate<Participant, bool>(jsFnOneOfAnd, engine, TranslationOpts);
        Console.WriteLine($"  Expression : {exprOneOfAnd}");
        SandboxStore.PrintSql(session.Query<Participant>().Where(exprOneOfAnd));
        var filteredOneOf = session.Query<Participant>().Where(exprOneOfAnd).ToList();
        Console.WriteLine($"  Rows ({filteredOneOf.Count}): {string.Join(", ", filteredOneOf.Select(p => p.Name))}");
    }
}
