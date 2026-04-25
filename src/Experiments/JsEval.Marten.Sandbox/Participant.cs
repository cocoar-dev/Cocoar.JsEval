namespace Cocoar.JsEval.Marten.Sandbox;

/// <summary>
/// Flat Marten document with a string discriminator field.
/// Type.Is(p, 'person') → WHERE data->>'ParticipantType' = 'person'
/// </summary>
public class Participant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ParticipantType { get; set; } = "";  // "person" | "company"

    // Person-specific (null for companies)
    public string? Firstname { get; set; }
    public string? Lastname  { get; set; }

    // Company-specific (null for persons)
    public string? VatNumber { get; set; }
}

/// <summary>
/// View type used only for Monaco IntelliSense narrowing.
/// Not stored in the DB — just tells TsDefinition what properties are available
/// after Type.Is(p, 'person') narrows the type.
/// </summary>
public class PersonView  : Participant { }

/// <summary>View type for Monaco narrowing after Type.Is(p, 'company').</summary>
public class CompanyView : Participant { }
