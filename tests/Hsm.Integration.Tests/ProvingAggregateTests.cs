using Hsm.Application.Proving;
using Hsm.Domain.Proving;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

/// <summary>
/// Proves the change-tracking behavior the stack was chosen for, against
/// real PostgreSQL: an aggregate round-trips, and removing a child through
/// the aggregate deletes the row instead of orphaning it.
/// </summary>
public sealed class ProvingAggregateTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _provider = TestServices.Build();
        await TestServices.MigrateAsync(_provider);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Aggregate_round_trips_and_child_removal_deletes_the_row()
    {
        var id = Guid.NewGuid();
        Guid keptItem;
        Guid removedItem;

        // Create through one scope (fresh change tracker per scope).
        using (var scope = _provider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IProvingRepository>();
            var root = new ProvingRoot(id, "change-tracking-proof");
            keptItem = root.AddItem("kept").Id;
            removedItem = root.AddItem("removed").Id;
            await repository.AddAsync(root);
            await repository.SaveChangesAsync();
        }

        // Reload in a second scope, remove one child through the aggregate.
        using (var scope = _provider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IProvingRepository>();
            var root = await repository.GetAsync(id);
            Assert.NotNull(root);
            Assert.Equal(2, root.Items.Count);

            root.RemoveItem(removedItem);
            await repository.SaveChangesAsync();
        }

        // Verify against the database directly: the removed child's row is
        // gone, not orphaned with a null FK.
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
            var labels = await db.Set<ProvingItem>()
                .Where(i => i.Id == keptItem || i.Id == removedItem)
                .Select(i => i.Id)
                .ToListAsync();

            Assert.Equal([keptItem], labels);
        }
    }
}
