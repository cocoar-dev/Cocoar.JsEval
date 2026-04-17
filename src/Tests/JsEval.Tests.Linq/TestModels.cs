namespace Cocoar.JsEval.Tests.Linq;

public enum UserStatus { None, Active, Archived, Deleted }

public class TestUser
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public int Age { get; set; }
    public long ExternalId { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTimeOffset LastLogin { get; set; }
    public DateOnly Birthday { get; set; }
    public TimeSpan SessionTimeout { get; set; }
    public UserStatus Status { get; set; }
    public List<string> Tags { get; set; } = [];
    public TestAddress? Address { get; set; }
}

public class TestAddress
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}
