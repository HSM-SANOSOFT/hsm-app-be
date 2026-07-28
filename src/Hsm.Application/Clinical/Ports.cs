using Hsm.Domain.Clinical;

namespace Hsm.Application.Clinical;

/// <summary>
/// Patient persistence port (plan U16). Identifier search binds parameters —
/// never interpolates — mirroring the frozen service's TypeORM discipline.
/// </summary>
public interface IPatientStore
{
    /// <summary>The patient with its identifier rows, or null.</summary>
    Task<Patient?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Patients holding an identifier row matching the token: value always,
    /// system additionally when provided (frozen searchByIdentifier).
    /// </summary>
    Task<IReadOnlyList<Patient>> SearchByIdentifierAsync(
        string? system, string value, CancellationToken ct = default);

    /// <summary>True when any (system, value) pair is already registered.</summary>
    Task<bool> AnyIdentifierExistsAsync(
        IReadOnlyList<(string System, string Value)> identifiers, CancellationToken ct = default);

    Task AddAsync(Patient patient, CancellationToken ct = default);
}
