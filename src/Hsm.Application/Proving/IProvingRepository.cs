using Hsm.Domain.Proving;

namespace Hsm.Application.Proving;

/// <summary>
/// Repository port for the throwaway proving aggregate. The port pattern for
/// real aggregates: bound concretely to PostgreSQL semantics (the interface
/// IS the port — no engine-agnostic data layer), one repository per
/// aggregate root, save is explicit.
/// </summary>
public interface IProvingRepository
{
    Task<ProvingRoot?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(ProvingRoot root, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
