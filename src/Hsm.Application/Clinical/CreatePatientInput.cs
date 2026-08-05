namespace Hsm.Application.Clinical;

/// <summary>One inbound FHIR Identifier, already reduced to the persisted shape.</summary>
public sealed record PatientIdentifierInput(string System, string Value, string? Use);

/// <summary>
/// A validated inbound FHIR Patient reduced to the persisted fields:
/// valid-but-unmapped R4 fields are deliberately dropped, and identifiers
/// lacking system or value are already filtered out by the caller.
/// </summary>
public sealed record CreatePatientInput(
    bool Active,
    string? Gender,
    string? BirthDate,
    string? NameJson,
    string? TelecomJson,
    string? AddressJson,
    IReadOnlyList<PatientIdentifierInput> Identifiers);
