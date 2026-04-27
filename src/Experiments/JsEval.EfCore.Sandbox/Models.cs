using Microsoft.EntityFrameworkCore;

namespace Cocoar.JsEval.EfCore.Sandbox;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public int Age { get; set; }
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

// Nested-navigation models for the optional-chaining scenario.
// Author --(optional)--> Person; Todo --(optional)--> Customer.
public class Author
{
    public int Id { get; set; }
    public string Type { get; set; } = "";   // "Person" | "Company"
    public bool IsActive { get; set; }
    public int? PersonId { get; set; }
    public Person? Person { get; set; }
}

public class Person
{
    public int Id { get; set; }
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }
}

public class Todo
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}

public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

// Polymorphic hierarchy for discriminator-mapping sandbox scenario.
// EF Core maps this as TPH with a 'Discriminator' column.
public abstract class Participant
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class PersonParticipant : Participant
{
    public string? Firstname { get; set; }
    public string? Lastname  { get; set; }
    public string? Email     { get; set; }
}

public class CompanyParticipant : Participant
{
    public string? VatNumber { get; set; }
    public string? Email     { get; set; }
}

/// <summary>Service accounts — no Email, used to verify the intersection boundary.</summary>
public class ServiceAccountParticipant : Participant
{
    public string? ServiceName { get; set; }
}

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }
    public DbSet<User> Users => Set<User>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<Todo> Todos => Set<Todo>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Participant> Participants => Set<Participant>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Author>()
            .HasOne(a => a.Person).WithMany()
            .HasForeignKey(a => a.PersonId);
        mb.Entity<Todo>()
            .HasOne(t => t.Customer).WithMany()
            .HasForeignKey(t => t.CustomerId);
        mb.Entity<Customer>().Property(c => c.Id).ValueGeneratedNever();
        mb.Entity<Participant>()
            .HasDiscriminator<string>("Discriminator")
            .HasValue<PersonParticipant>("person")
            .HasValue<CompanyParticipant>("company")
            .HasValue<ServiceAccountParticipant>("service-account");
    }
}
