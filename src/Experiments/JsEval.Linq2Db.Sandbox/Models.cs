using LinqToDB.Mapping;

namespace Cocoar.JsEval.Linq2Db.Sandbox;

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
