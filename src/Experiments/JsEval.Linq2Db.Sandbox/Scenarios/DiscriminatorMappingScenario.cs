using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq;
using Jint;
using LinqToDB;
using LinqToDB.Data;

namespace Cocoar.JsEval.Linq2Db.Sandbox.Scenarios;

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

    public static void Seed(DataConnection db)
    {
        db.Execute(@"CREATE TABLE IF NOT EXISTS Participants (
            Id           INTEGER PRIMARY KEY AUTOINCREMENT,
            Name         TEXT NOT NULL,
            Discriminator TEXT NOT NULL,
            Firstname    TEXT,
            Lastname     TEXT,
            VatNumber    TEXT
        )");
        db.GetTable<PersonParticipant>().BulkCopy(
        [
            new PersonParticipant { Name = "Alice Müller", Firstname = "Alice",   Lastname = "Müller",  Discriminator = "person" },
            new PersonParticipant { Name = "Bob Huber",    Firstname = "Bob",     Lastname = "Huber",   Discriminator = "person" },
            new PersonParticipant { Name = "Anna Schmidt", Firstname = "Anna",    Lastname = "Schmidt", Discriminator = "person" },
        ]);
        db.GetTable<CompanyParticipant>().BulkCopy(
        [
            new CompanyParticipant { Name = "Acme Corp",    VatNumber = "ATU1234", Discriminator = "company" },
            new CompanyParticipant { Name = "Widgets GmbH", VatNumber = "ATU5678", Discriminator = "company" },
        ]);
    }

    public static void Run(DataConnection db)
    {
        var engine = new Jint.Engine();
        engine.SetValue("Type", new JsTypeGlobal(Mappings, new Dictionary<string, Type>(), []));

        var person  = new PersonParticipant  { Name = "X" };
        var company = new CompanyParticipant { Name = "Y" };
        engine.SetValue("person",  person);
        engine.SetValue("company", company);

        // ── 1. Runtime ───────────────────────────────────────────────────────
        Console.WriteLine("Runtime Type.Is / Type.IsOneOf:");
        Console.WriteLine($"  Type.Is(PersonParticipant,  'person')  -> {engine.Evaluate("Type.Is(person, 'person')")}");
        Console.WriteLine($"  Type.Is(CompanyParticipant, 'person')  -> {engine.Evaluate("Type.Is(company, 'person')")}");
        Console.WriteLine($"  Type.IsOneOf(person,  ['person','company']) -> {engine.Evaluate("Type.IsOneOf(person,  ['person','company'])")}");
        Console.WriteLine($"  Type.IsOneOf(company, ['person','company']) -> {engine.Evaluate("Type.IsOneOf(company, ['person','company'])")}");

        // ── 2. LINQ: Type.Is → SQL ───────────────────────────────────────────
        Console.WriteLine("\nLINQ Type.Is → LINQ2DB SQL:");
        var jsFn = engine.Evaluate("(p) => Type.Is(p, 'person')");
        var expr  = JsExpressionTranslator.Translate<Participant, bool>(jsFn, engine, TranslationOpts);
        var query = db.GetTable<Participant>().Where(expr);
        Console.WriteLine($"  Expression : {expr}");
        Console.WriteLine($"  SQL        : {query.ToSqlQuery().Sql.Replace("\n\t", " ")}");
        var persons = query.ToList();
        Console.WriteLine($"  Rows ({persons.Count}): {string.Join(", ", persons.Select(p => p.Name))}");

        // ── 3. LINQ: Type.IsOneOf → SQL ──────────────────────────────────────
        Console.WriteLine("\nLINQ Type.IsOneOf → LINQ2DB SQL:");
        var jsFnOneOf = engine.Evaluate("(p) => Type.IsOneOf(p, ['person', 'company'])");
        var exprOneOf = JsExpressionTranslator.Translate<Participant, bool>(jsFnOneOf, engine, TranslationOpts);
        var queryOneOf = db.GetTable<Participant>().Where(exprOneOf);
        Console.WriteLine($"  Expression : {exprOneOf}");
        Console.WriteLine($"  SQL        : {queryOneOf.ToSqlQuery().Sql.Replace("\n\t", " ")}");
        var all = queryOneOf.ToList();
        Console.WriteLine($"  Rows ({all.Count}): {string.Join(", ", all.Select(p => p.Name))}");

        // ── 4. LINQ: AND-narrowing ───────────────────────────────────────────
        Console.WriteLine("\nLINQ AND-narrowing (Type.Is + subtype property):");
        var jsFnNarrow = engine.Evaluate("(p) => Type.Is(p, 'person') && p.Firstname.startsWith('A')");
        var exprNarrow = JsExpressionTranslator.Translate<Participant, bool>(jsFnNarrow, engine, TranslationOpts);
        var queryNarrow = db.GetTable<Participant>().Where(exprNarrow);
        Console.WriteLine($"  Expression : {exprNarrow}");
        Console.WriteLine($"  SQL        : {queryNarrow.ToSqlQuery().Sql.Replace("\n\t", " ")}");
        var narrowed = queryNarrow.ToList();
        Console.WriteLine($"  Rows ({narrowed.Count}): {string.Join(", ", narrowed.Select(p => p.Name))}");

        // ── 5. LINQ: IsOneOf AND-narrowing ───────────────────────────────────
        Console.WriteLine("\nLINQ IsOneOf AND-narrowing:");
        var jsFnOneOfNarrow = engine.Evaluate("(p) => Type.IsOneOf(p, ['person']) && p.Firstname.startsWith('A')");
        var exprOneOfNarrow = JsExpressionTranslator.Translate<Participant, bool>(jsFnOneOfNarrow, engine, TranslationOpts);
        Console.WriteLine($"  Expression : {exprOneOfNarrow}");
        var narrowedOneOf = db.GetTable<Participant>().Where(exprOneOfNarrow).ToList();
        Console.WriteLine($"  Rows ({narrowedOneOf.Count}): {string.Join(", ", narrowedOneOf.Select(p => p.Name))}");
    }
}
