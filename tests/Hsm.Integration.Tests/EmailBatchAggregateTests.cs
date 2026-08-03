using Hsm.Domain.Coms;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

/// <summary>
/// Proves the U14 batch/recipient aggregate against real PostgreSQL: the
/// parent-child relationship round-trips, and removing a recipient THROUGH
/// THE BATCH AGGREGATE deletes the row (EF Core change tracking / orphan
/// removal) instead of leaving it behind with a dangling FK.
/// </summary>
public sealed class EmailBatchAggregateTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        // A database dedicated to this suite: dropped and migrated back per
        // run, so no earlier run's rows can be mistaken for this one's.
        var configured = TestServices.Configuration["ConnectionStrings:HsmDb"]!;
        var dedicated = string.Join(
            ';',
            configured
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
                .Append("Database=hsm_coms_aggregate_test"));
        _provider = TestServices.Build(builder =>
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HsmDb"] = dedicated,
            }));

        await TestServices.RecreateAsync(_provider);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Removing_a_recipient_through_the_batch_aggregate_deletes_the_row()
    {
        var batchId = Guid.NewGuid();
        Guid keptRecipient;
        Guid removedRecipient;

        // Create the aggregate in one scope.
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
            var batch = new EmailBatch
            {
                Id = batchId,
                DataJson = """{"patientName":"Ada"}""",
                OverallStatus = EmailBatchStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var kept = new EmailRecipient
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                ToEmail = "kept@integration.test",
            };
            var removed = new EmailRecipient
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                ToEmail = "removed@integration.test",
            };
            batch.Recipients.Add(kept);
            batch.Recipients.Add(removed);
            keptRecipient = kept.Id;
            removedRecipient = removed.Id;
            db.EmailBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        // Reload in a second scope (fresh change tracker), remove one child
        // from the aggregate's collection — and ONLY that.
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
            var batch = await db.EmailBatches
                .Include(b => b.Recipients)
                .SingleAsync(b => b.Id == batchId);
            Assert.Equal(2, batch.Recipients.Count);

            batch.Recipients.Remove(batch.Recipients.Single(r => r.Id == removedRecipient));
            await db.SaveChangesAsync();
        }

        // Verify against the database directly: the removed child's ROW is
        // gone (deleted, not orphaned with a null FK).
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
            var remaining = await db.EmailRecipients
                .Where(r => r.Id == keptRecipient || r.Id == removedRecipient)
                .Select(r => r.Id)
                .ToListAsync();

            Assert.Equal([keptRecipient], remaining);
        }
    }
}
