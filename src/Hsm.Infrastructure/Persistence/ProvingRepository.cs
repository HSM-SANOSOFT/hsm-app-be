using Hsm.Application.Proving;
using Hsm.Domain.Proving;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Persistence;

public sealed class ProvingRepository(HsmDbContext dbContext) : IProvingRepository
{
    public Task<ProvingRoot?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.ProvingRoots
            .Include(r => r.Items)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task AddAsync(ProvingRoot root, CancellationToken cancellationToken = default)
        => await dbContext.ProvingRoots.AddAsync(root, cancellationToken).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
