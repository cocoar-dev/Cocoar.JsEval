namespace Cocoar.JsEval.Marten.Sandbox;

public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public int Age { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Tags { get; set; } = [];
    public Address? Address { get; set; }
}

public class Address
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}
