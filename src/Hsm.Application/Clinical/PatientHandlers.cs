using Hsm.Application.Errors;
using Hsm.Domain.Clinical;

namespace Hsm.Application.Clinical;

/// <summary>One inbound FHIR Identifier, already reduced to the persisted shape.</summary>
public sealed record PatientIdentifierInput(string System, string Value, string? Use);

/// <summary>
/// A validated inbound FHIR Patient reduced to the frozen persisted fields
/// (patient.translator.ts fromFhir): valid-but-unmapped R4 fields are
/// deliberately dropped, and identifiers lacking system or value are already
/// filtered out by the caller.
/// </summary>
public sealed record CreatePatientInput(
    bool Active,
    string? Gender,
    string? BirthDate,
    string? NameJson,
    string? TelecomJson,
    string? AddressJson,
    IReadOnlyList<PatientIdentifierInput> Identifiers);

/// <summary>
/// Frozen PatientService.create: persist the patient with its identifier rows;
/// a (system, value) already registered is the frozen 409
/// "A patient with one of these identifiers already exists".
/// </summary>
public sealed class CreatePatientHandler(IPatientStore patients)
{
    public async Task<Patient> HandleAsync(CreatePatientInput input, CancellationToken ct = default)
    {
        var pairs = input.Identifiers.Select(i => (i.System, i.Value)).ToList();
        if (pairs.Count > 0 && await patients.AnyIdentifierExistsAsync(pairs, ct))
        {
            throw new ApiException(
                409,
                "A patient with one of these identifiers already exists",
                errorLabel: "Conflict");
        }

        var now = DateTimeOffset.UtcNow;
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            Active = input.Active,
            Gender = input.Gender,
            BirthDate = input.BirthDate,
            NameJson = input.NameJson,
            TelecomJson = input.TelecomJson,
            AddressJson = input.AddressJson,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var identifier in input.Identifiers)
        {
            patient.Identifiers.Add(new PatientIdentifier
            {
                Id = Guid.NewGuid(),
                PatientId = patient.Id,
                System = identifier.System,
                Value = identifier.Value,
                Use = identifier.Use,
            });
        }

        await patients.AddAsync(patient, ct);
        return patient;
    }
}

/// <summary>
/// Frozen PatientService.getByIdAsFhir: read by logical id, 404 with the
/// frozen message when absent. A non-uuid id is not-found here too — the
/// frozen implementation surfaced a raw database error as a 500 for that
/// input; a deliberate, documented divergence.
/// </summary>
public sealed class GetPatientHandler(IPatientStore patients)
{
    public async Task<Patient> HandleAsync(string id, CancellationToken ct = default)
    {
        var patient = Guid.TryParse(id, out var parsed)
            ? await patients.FindByIdAsync(parsed, ct)
            : null;
        return patient ?? throw new ApiException(404, $"Patient '{id}' not found", errorLabel: "Not Found");
    }
}

/// <summary>
/// Frozen PatientService.searchByIdentifier: match value always, system when
/// the token carried one. Unknown identifiers are an EMPTY result — the
/// search's not-found behavior is an empty Bundle, never a 404.
/// </summary>
public sealed class SearchPatientsHandler(IPatientStore patients)
{
    public Task<IReadOnlyList<Patient>> HandleAsync(
        string? system, string value, CancellationToken ct = default) =>
        patients.SearchByIdentifierAsync(system, value, ct);
}
