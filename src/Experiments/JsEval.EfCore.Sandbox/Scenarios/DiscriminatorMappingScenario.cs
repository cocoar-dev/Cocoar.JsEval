using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Microsoft.EntityFrameworkCore;

namespace Cocoar.JsEval.EfCore.Sandbox.Scenarios;

public static class DiscriminatorMappingScenario
{
    private static readonly List<DiscriminatorMapping> Mappings =
    [
        new(typeof(Participant), "person",  typeof(PersonParticipant)),
        new(typeof(Participant), "company", typeof(CompanyParticipant)),
    ];

    private static readonly TranslationOptions TranslationOpts = new()
    {
        DiscriminatorMappings = Mappings
    };

    public static async Task Seed(AppDb db)
    {
        db.Participants.AddRange(
            new PersonParticipant  { Name = "Alice Müller",   Firstname = "Alice",   Lastname = "Müller" },
            new PersonParticipant  { Name = "Bob Huber",      Firstname = "Bob",     Lastname = "Huber" },
            new CompanyParticipant { Name = "Acme Corp",      VatNumber = "ATU1234" },
            new CompanyParticipant { Name = "Widgets GmbH",   VatNumber = "ATU5678" },
            new PersonParticipant  { Name = "Anna Schmidt",   Firstname = "Anna",    Lastname = "Schmidt" }
        );
        await db.SaveChangesAsync();
    }

    public static async Task Run(AppDb db)
    {
        var engine = new Jint.Engine();
        // Register Type global manually (same object JsEngine would register automatically).
        engine.SetValue("Type", new JsTypeGlobal(Mappings, new Dictionary<string, Type>(), []));

        var person  = new PersonParticipant  { Name = "X" };
        var company = new CompanyParticipant { Name = "Y" };
        engine.SetValue("person",  person);
        engine.SetValue("company", company);

        // ── 1. Runtime: Type.Is ──────────────────────────────────────────────
        Console.WriteLine("Runtime Type.Is:");
        Console.WriteLine($"  Type.Is(PersonParticipant,  'person')  -> {engine.Evaluate("Type.Is(person,  'person')")}");
        Console.WriteLine($"  Type.Is(CompanyParticipant, 'person')  -> {engine.Evaluate("Type.Is(company, 'person')")}");
        Console.WriteLine($"  Type.Is(PersonParticipant,  'company') -> {engine.Evaluate("Type.Is(person,  'company')")}");

        // ── 2. Runtime: Type.IsOneOf ─────────────────────────────────────────
        Console.WriteLine("\nRuntime Type.IsOneOf:");
        Console.WriteLine($"  Type.IsOneOf(person,  ['person','company']) -> {engine.Evaluate("Type.IsOneOf(person,  ['person','company'])")}");
        Console.WriteLine($"  Type.IsOneOf(company, ['person','company']) -> {engine.Evaluate("Type.IsOneOf(company, ['person','company'])")}");

        // ── 3. LINQ: Type.Is → EF Core SQL ──────────────────────────────────
        Console.WriteLine("\nLINQ Type.Is → EF Core SQL:");
        var jsFn = engine.Evaluate("(p) => Type.Is(p, 'person')");
        var expr  = JsExpressionTranslator.Translate<Participant, bool>(jsFn, engine, TranslationOpts);
        var sql   = db.Participants.AsNoTracking().Where(expr).ToQueryString();
        Console.WriteLine($"  Expression : {expr}");
        Console.WriteLine($"  SQL        : {sql.Replace("\n", " ")}");
        var persons = await db.Participants.AsNoTracking().Where(expr).ToListAsync();
        Console.WriteLine($"  Rows ({persons.Count}): {string.Join(", ", persons.Select(p => p.Name))}");

        // ── 4. LINQ: Type.IsOneOf → EF Core SQL ─────────────────────────────
        Console.WriteLine("\nLINQ Type.IsOneOf → EF Core SQL:");
        var jsFnOneOf = engine.Evaluate("(p) => Type.IsOneOf(p, ['person', 'company'])");
        var exprOneOf = JsExpressionTranslator.Translate<Participant, bool>(jsFnOneOf, engine, TranslationOpts);
        var sqlOneOf  = db.Participants.AsNoTracking().Where(exprOneOf).ToQueryString();
        Console.WriteLine($"  Expression : {exprOneOf}");
        Console.WriteLine($"  SQL        : {sqlOneOf.Replace("\n", " ")}");
        var all = await db.Participants.AsNoTracking().Where(exprOneOf).ToListAsync();
        Console.WriteLine($"  Rows ({all.Count}): {string.Join(", ", all.Select(p => p.Name))}");

        // ── 5. LINQ: AND-narrowing accesses derived-type property ────────────
        Console.WriteLine("\nLINQ AND-narrowing (Type.Is + subtype property):");
        var jsFnNarrow = engine.Evaluate("(p) => Type.Is(p, 'person') && p.Firstname.startsWith('A')");
        var exprNarrow = JsExpressionTranslator.Translate<Participant, bool>(jsFnNarrow, engine, TranslationOpts);
        var sqlNarrow  = db.Participants.AsNoTracking().Where(exprNarrow).ToQueryString();
        Console.WriteLine($"  Expression : {exprNarrow}");
        Console.WriteLine($"  SQL        : {sqlNarrow.Replace("\n", " ")}");
        var narrowed = await db.Participants.AsNoTracking().Where(exprNarrow).ToListAsync();
        Console.WriteLine($"  Rows ({narrowed.Count}): {string.Join(", ", narrowed.Select(p => p.Name))}");

        // ── 6. LINQ: IsOneOf AND-narrowing ───────────────────────────────────
        Console.WriteLine("\nLINQ IsOneOf AND-narrowing:");
        var jsFnOneOfNarrow = engine.Evaluate("(p) => Type.IsOneOf(p, ['person']) && p.Firstname.startsWith('A')");
        var exprOneOfNarrow = JsExpressionTranslator.Translate<Participant, bool>(jsFnOneOfNarrow, engine, TranslationOpts);
        Console.WriteLine($"  Expression : {exprOneOfNarrow}");
        var narrowedOneOf = await db.Participants.AsNoTracking().Where(exprOneOfNarrow).ToListAsync();
        Console.WriteLine($"  Rows ({narrowedOneOf.Count}): {string.Join(", ", narrowedOneOf.Select(p => p.Name))}");

        await Task.CompletedTask;
    }
}
