using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Clinical;

namespace Hsm.Application.Clinical.Queries.GetPatient;

/// <summary>
/// Frozen PatientService.getByIdAsFhir: read by logical id, 404 with the
/// frozen message when absent. A non-uuid id is not-found here too — the
/// frozen implementation surfaced a raw database error as a 500 for that
/// input; a deliberate, documented divergence.
/// </summary>
public sealed class GetPatientHandler(IPatientStore patients) : IRequestHandler<GetPatientQuery, Patient>
{
    public async Task<Patient> HandleAsync(GetPatientQuery request, CancellationToken ct)
    {
        var patient = Guid.TryParse(request.Id, out var parsed)
            ? await patients.FindByIdAsync(parsed, ct)
            : null;
        return patient ?? throw new NotFoundException("Patient", request.Id);
    }
}
