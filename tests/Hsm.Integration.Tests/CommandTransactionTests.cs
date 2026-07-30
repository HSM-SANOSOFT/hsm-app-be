using Hsm.Application.Abstractions;
using Hsm.Application.Proving;
using Hsm.Domain.Proving;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

/// <summary>
/// Proves TransactionBehavior against real PostgreSQL: a command handler writes
/// a row and then throws, all inside the pipeline's transaction boundary. The
/// row must not survive — the transaction is rolled back by the failing
/// handler, not committed by a stray SaveChanges outside the boundary.
/// </summary>
public sealed class CommandTransactionTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    /// <summary>A command with no meaningful authorization story for this test — it
    /// only needs to prove the transaction boundary, so it opts out of the
    /// AuthorizationBehavior via AllowAnonymousRequest rather than installing an
    /// actor through AmbientPrincipal.</summary>
    [AllowAnonymousRequest]
    private sealed record WriteThenThrow(Guid RootId) : ICommand<Unit>;

    private sealed class WriteThenThrowHandler(IProvingRepository repository) : IRequestHandler<WriteThenThrow, Unit>
    {
        public async Task<Unit> HandleAsync(WriteThenThrow request, CancellationToken ct)
        {
            await repository.AddAsync(new ProvingRoot(request.RootId, "rolled-back"), ct);
            await repository.SaveChangesAsync(ct);
            throw new InvalidOperationException("Induced failure after the write, inside the transaction.");
        }
    }

    public async Task InitializeAsync()
    {
        _provider = TestServices.Build(customizeServices: services =>
        {
            services.AddHsmPipeline();
            services.AddScoped<ICurrentPrincipal, AmbientPrincipal>();
            services.AddScoped<IRequestHandler<WriteThenThrow, Unit>, WriteThenThrowHandler>();
        });

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Failing_command_rolls_back_its_write()
    {
        var rootId = Guid.NewGuid();

        using (var scope = _provider.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dispatcher.Send<Unit>(new WriteThenThrow(rootId), CancellationToken.None));
        }

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
            var persisted = await db.Set<ProvingRoot>().SingleOrDefaultAsync(r => r.Id == rootId);
            Assert.Null(persisted);
        }
    }
}
