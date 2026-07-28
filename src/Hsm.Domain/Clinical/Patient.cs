namespace Hsm.Domain.Clinical;

/// <summary>
/// FHIR Patient system-of-record entity, pg-native (frozen patient.entity.ts,
/// KTD1/KTD4). Searchable scalars are real columns; complex FHIR datatypes not
/// yet searched on (name[], telecom[], address[]) persist as jsonb documents
/// carrying the inbound FHIR JSON verbatim. Business identifiers live in the
/// normalized child table — never as the primary key (the MPI seam, KTD2).
/// </summary>
public class Patient
{
    /// <summary>Server-assigned uuid = FHIR Resource.id = relative reference target.</summary>
    public Guid Id { get; set; }

    public bool Active { get; set; } = true;

    public string? Gender { get; set; }

    /// <summary>FHIR date (YYYY, YYYY-MM, or YYYY-MM-DD) — text to preserve partials.</summary>
    public string? BirthDate { get; set; }

    /// <summary>FHIR HumanName[] as a jsonb document (null when absent).</summary>
    public string? NameJson { get; set; }

    /// <summary>FHIR ContactPoint[] as a jsonb document (null when absent).</summary>
    public string? TelecomJson { get; set; }

    /// <summary>FHIR Address[] as a jsonb document (null when absent).</summary>
    public string? AddressJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public List<PatientIdentifier> Identifiers { get; set; } = [];
}

/// <summary>
/// Normalized FHIR Identifier[] child row — the national-identifier lookup
/// seam (frozen patient-identifier.entity.ts). Unique (system, value) so the
/// same business id can never be registered twice.
/// </summary>
public class PatientIdentifier
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }

    /// <summary>FHIR Identifier.system (the namespace URI).</summary>
    public string System { get; set; } = string.Empty;

    /// <summary>FHIR Identifier.value (the business id within the system).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>FHIR Identifier.use (usual | official | temp | secondary | old).</summary>
    public string? Use { get; set; }
}
