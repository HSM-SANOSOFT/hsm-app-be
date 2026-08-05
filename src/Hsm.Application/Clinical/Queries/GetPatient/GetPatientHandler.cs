using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Clinical;

namespace Hsm.Application.Clinical.Queries.GetPatient;

/// <summary>
/// Read by logical id, 404 when absent. A non-uuid id is not-found here too,
/// rather than surfacing a raw database error as a 500 for that input — a
/// deliberate, documented choice so a malformed identifier never leaks
/// internal error detail.
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
