namespace Cocoar.JsEval.Marten.Sandbox;

/// <summary>
/// Flat Marten document with a string discriminator field.
/// Type.Is(p, 'person') → WHERE data->>'ParticipantType' = 'person'
/// </summary>
public class Participant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ParticipantType { get; set; } = "";  // "person" | "company" | "service-account"

    // Person-specific
    public string? Firstname { get; set; }
    public string? Lastname  { get; set; }

    // Company-specific
    public string? VatNumber { get; set; }

    // Person + Company (null for service-accounts)
    public string? Email { get; set; }
}

/// <summary>
/// View type used only for Monaco IntelliSense narrowing.
/// Not stored in the DB — just tells TsDefinition what properties are available
/// after Type.Is(p, 'person') narrows the type.
/// </summary>
public class PersonView  : Participant { public new string? Email { get; set; } }

/// <summary>View type for Monaco narrowing after Type.Is(p, 'company').</summary>
public class CompanyView : Participant { public new string? Email { get; set; } }

/// <summary>Service accounts — no Email, used to verify the intersection boundary.</summary>
public class ServiceAccountView : Participant { public string? ServiceName { get; set; } }
