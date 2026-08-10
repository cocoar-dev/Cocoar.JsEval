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
        new(typeof(Participant), "person",          typeof(PersonView),          "ParticipantType"),
        new(typeof(Participant), "company",         typeof(CompanyView),         "ParticipantType"),
        new(typeof(Participant), "service-account", typeof(ServiceAccountView),  "ParticipantType"),
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
            new Participant { Id = Guid.NewGuid(), Name = "Alice Müller",  ParticipantType = "person",         Firstname = "Alice", Lastname = "Müller",  Email = "alice@example.com"         },
            new Participant { Id = Guid.NewGuid(), Name = "Bob Huber",     ParticipantType = "person",         Firstname = "Bob",   Lastname = "Huber",   Email = "bob@other.com"             },
            new Participant { Id = Guid.NewGuid(), Name = "Anna Schmidt",  ParticipantType = "person",         Firstname = "Anna",  Lastname = "Schmidt", Email = "anna@example.com"          },
            new Participant { Id = Guid.NewGuid(), Name = "Acme Corp",     ParticipantType = "company",        VatNumber = "ATU1234",                     Email = "info@acme.example.com"     },
            new Participant { Id = Guid.NewGuid(), Name = "Widgets GmbH",  ParticipantType = "company",        VatNumber = "ATU5678",                     Email = "office@widgets.other.com"  },
            new Participant { Id = Guid.NewGuid(), Name = "deploy-bot",    ParticipantType = "service-account"                                                                                }
        );
        await session.SaveChangesAsync();
        Console.WriteLine("Seeded 5 participants (property-based discrimination).");
        Console.WriteLine();
    }

    public static async Task Run(IDocumentSession session)
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
        var persons = await session.Query<Participant>().Where(expr).ToListAsync();
        Console.WriteLine($"  Rows ({persons.Count}): {string.Join(", ", persons.Select(p => p.Name))}");

        // ── 3. LINQ: Type.IsOneOf → Marten SQL ──────────────────────────────
        Console.WriteLine("\nLINQ Type.IsOneOf → Marten SQL:");
        var jsFnOneOf = engine.Evaluate("(p) => Type.IsOneOf(p, ['person', 'company'])");
        var exprOneOf = JsExpressionTranslator.Translate<Participant, bool>(jsFnOneOf, engine, TranslationOpts);
        Console.WriteLine($"  Expression : {exprOneOf}");
        SandboxStore.PrintSql(session.Query<Participant>().Where(exprOneOf));
        var all = await session.Query<Participant>().Where(exprOneOf).ToListAsync();
        Console.WriteLine($"  Rows ({all.Count}): {string.Join(", ", all.Select(p => p.Name))}");

        // ── 4. LINQ: AND-narrowing ───────────────────────────────────────────
        Console.WriteLine("\nLINQ AND (Type.Is + property filter):");
        var jsFnAnd = engine.Evaluate("(p) => Type.Is(p, 'person') && p.Firstname.startsWith('A')");
        var exprAnd = JsExpressionTranslator.Translate<Participant, bool>(jsFnAnd, engine, TranslationOpts);
        Console.WriteLine($"  Expression : {exprAnd}");
        SandboxStore.PrintSql(session.Query<Participant>().Where(exprAnd));
        var filtered = await session.Query<Participant>().Where(exprAnd).ToListAsync();
        Console.WriteLine($"  Rows ({filtered.Count}): {string.Join(", ", filtered.Select(p => p.Name))}");

        // ── 5. LINQ: IsOneOf AND ─────────────────────────────────────────────
        Console.WriteLine("\nLINQ IsOneOf AND:");
        var jsFnOneOfAnd = engine.Evaluate("(p) => Type.IsOneOf(p, ['person']) && p.Firstname.startsWith('A')");
        var exprOneOfAnd = JsExpressionTranslator.Translate<Participant, bool>(jsFnOneOfAnd, engine, TranslationOpts);
        Console.WriteLine($"  Expression : {exprOneOfAnd}");
        SandboxStore.PrintSql(session.Query<Participant>().Where(exprOneOfAnd));
        var filteredOneOf = await session.Query<Participant>().Where(exprOneOfAnd).ToListAsync();
        Console.WriteLine($"  Rows ({filteredOneOf.Count}): {string.Join(", ", filteredOneOf.Select(p => p.Name))}");

        // ── 6. OR + AND intersection narrowing ───────────────────────────────
        // Person AND Company both have Email (on PersonView/CompanyView); ServiceAccount doesn't.
        // Email is NOT on the base Participant C# class used for the combined-mapping views,
        // but IS physically in the JSONB document (stored via Participant.Email).
        // The translator emits Convert(p, PersonView).Email — does Marten follow the cast?
        Console.WriteLine("\nOR + AND intersection narrowing (Email on person+company view types, not on service-account):");
        try
        {
            var jsFnIntersect = engine.Evaluate(
                "(p) => (Type.Is(p, 'person') || Type.Is(p, 'company')) && p.Email.endsWith('@example.com')");
            var exprIntersect = JsExpressionTranslator.Translate<Participant, bool>(jsFnIntersect, engine, TranslationOpts);
            Console.WriteLine($"  Expression : {exprIntersect}");
            SandboxStore.PrintSql(session.Query<Participant>().Where(exprIntersect));
            var intersected = await session.Query<Participant>().Where(exprIntersect).ToListAsync();
            Console.WriteLine($"  Rows ({intersected.Count}): {string.Join(", ", intersected.Select(p => p.Name))}");
            Console.WriteLine($"  service-account in result: {intersected.Any(p => p.ParticipantType == "service-account")} (expected: false)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }
}
