using Hsm.Application.Abstractions;
using Hsm.Domain.Clinical;

namespace Hsm.Application.Clinical.Queries.SearchPatients;

/// <summary>
/// Frozen PatientService.searchByIdentifier: match value always, system when
/// the token carried one. Unknown identifiers are an EMPTY result — the
/// search's not-found behavior is an empty Bundle, never a 404.
/// </summary>
public sealed class SearchPatientsHandler(IPatientStore patients)
    : IRequestHandler<SearchPatientsQuery, IReadOnlyList<Patient>>
{
    public Task<IReadOnlyList<Patient>> HandleAsync(SearchPatientsQuery request, CancellationToken ct) =>
        patients.SearchByIdentifierAsync(request.System, request.Value, ct);
}
