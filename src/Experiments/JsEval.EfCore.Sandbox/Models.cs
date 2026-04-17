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

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }
    public DbSet<User> Users => Set<User>();
}
