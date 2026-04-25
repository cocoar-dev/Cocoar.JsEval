using LinqToDB.Mapping;

namespace Cocoar.JsEval.Linq2Db.Sandbox;

// Polymorphic hierarchy for discriminator-mapping scenario.
// LinqToDB maps this as a single "Participants" table with a Discriminator column.
[InheritanceMapping(Code = "person",  Type = typeof(PersonParticipant))]
[InheritanceMapping(Code = "company", Type = typeof(CompanyParticipant))]
[Table("Participants")]
public abstract class Participant
{
    [PrimaryKey, Identity] public int Id { get; set; }
    [Column] public string Name { get; set; } = "";
    [Column(IsDiscriminator = true)] public string Discriminator { get; set; } = "";
}

[Table("Participants")]
public class PersonParticipant : Participant
{
    [Column] public string? Firstname { get; set; }
    [Column] public string? Lastname { get; set; }
}

[Table("Participants")]
public class CompanyParticipant : Participant
{
    [Column] public string? VatNumber { get; set; }
}

[Table("Users")]
public class User
{
    [PrimaryKey, Identity] public int Id { get; set; }
    [Column] public string Name { get; set; } = "";
    [Column] public string Email { get; set; } = "";
    [Column] public bool IsActive { get; set; }
    [Column] public int Age { get; set; }
    [Column] public string City { get; set; } = "";
    [Column] public string Zip { get; set; } = "";
}

// Nested-navigation models for the optional-chaining scenario. Associations
// are declared via [Association] — LINQ2DB's equivalent of an EF navigation
// property, translated to LEFT JOINs when traversed in a query.
[Table("Authors")]
public class Author
{
    [PrimaryKey, Identity] public int Id { get; set; }
    [Column] public string Type { get; set; } = "";     // "Person" | "Company"
    [Column] public bool IsActive { get; set; }
    [Column] public int? PersonId { get; set; }
    [Association(ThisKey = nameof(PersonId), OtherKey = nameof(Sandbox.Person.Id), CanBeNull = true)]
    public Person? Person { get; set; }
}

[Table("Persons")]
public class Person
{
    [PrimaryKey, Identity] public int Id { get; set; }
    [Column] public string? Firstname { get; set; }
    [Column] public string? Lastname { get; set; }
}

[Table("Todos")]
public class Todo
{
    [PrimaryKey, Identity] public int Id { get; set; }
    [Column] public string Title { get; set; } = "";
    [Column] public Guid? CustomerId { get; set; }
    [Association(ThisKey = nameof(CustomerId), OtherKey = nameof(Sandbox.Customer.Id), CanBeNull = true)]
    public Customer? Customer { get; set; }
}

[Table("Customers")]
public class Customer
{
    [PrimaryKey] public Guid Id { get; set; }
    [Column] public string Name { get; set; } = "";
}
