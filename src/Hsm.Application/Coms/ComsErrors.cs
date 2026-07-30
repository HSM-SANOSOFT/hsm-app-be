using Hsm.Application.Errors;

namespace Hsm.Application.Coms;

/// <summary>Shared 404 factories for the coms read surface.</summary>
/// <remarks>
/// The frozen source *intended* 404 for unknown ids (findOneOrFail with a
/// "→ 404" comment, and the contract snapshot declares 404 on all four {id}
/// operations) but its exception filter never caught EntityNotFoundError, so
/// the runtime leaked a 500. The snapshot's declared 404 is honored here — a
/// deliberate, documented divergence from the frozen bug.
/// </remarks>
public static class ComsErrors
{
    public static ApiException BatchNotFound(Guid id) =>
        ApiException.NotFound($"Email batch with id {id} not found");

    public static ApiException RecipientNotFound(Guid id) =>
        ApiException.NotFound($"Email recipient with id {id} not found");
}
