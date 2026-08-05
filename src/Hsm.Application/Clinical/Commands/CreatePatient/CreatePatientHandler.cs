using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Clinical;

namespace Hsm.Application.Clinical.Commands.CreatePatient;

/// <summary>
/// Persist the patient with its identifier rows;
/// a (system, value) already registered is a 409
/// "A patient with one of these identifiers already exists".
/// </summary>
public sealed class CreatePatientHandler(IPatientStore patients) : IRequestHandler<CreatePatientCommand, Patient>
{
    public async Task<Patient> HandleAsync(CreatePatientCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var pairs = input.Identifiers.Select(i => (i.System, i.Value)).ToList();
        if (pairs.Count > 0 && await patients.AnyIdentifierExistsAsync(pairs, ct))
        {
            throw new ConflictException("A patient with one of these identifiers already exists");
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
