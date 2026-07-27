using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

/// <summary>
/// Proves the role-replacement transactional boundary (plan U13 / DoD C2)
/// against real PostgreSQL: the frozen changeUserRole ran delete-then-insert
/// inside ONE transaction, so an induced failure after the delete must leave
/// the original role rows intact — never a user stripped of roles.
/// </summary>
public sealed class RoleReplacementTransactionTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        // A database dedicated to this suite: the schema is recreated per run,
        // which EnsureCreated alone would skip on a stale shared database.
        var configured = TestServices.Configuration["ConnectionStrings:HsmDb"]!;
        var dedicated = string.Join(
            ';',
            configured
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
                .Append("Database=hsm_roles_test"));
        _provider = TestServices.Build(builder =>
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HsmDb"] = dedicated,
            }));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Failed_role_replacement_rolls_back_to_the_original_assignment()
    {
        Guid userId;
        using (var scope = _provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IAuthUnitOfWork>();
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = $"txn_{Guid.NewGuid():N}",
                Email = $"txn_{Guid.NewGuid():N}@integration.test",
                PasswordHash = "not-a-real-hash",
                FirstName = "Txn",
                FirstLastName = "Proof",
                OnboardingCompletedAt = DateTimeOffset.UtcNow,
            };
            await users.AddAsync(user, [Roles.Doctor], default);
            await unitOfWork.SaveChangesAsync();
            userId = user.Id;
        }

        // Induce a failure INSIDE the boundary: a duplicated pair violates the
        // (user, domain, role) unique index on insert — after the delete of the
        // original rows has already executed within the same transaction.
        using (var scope = _provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IAuthUnitOfWork>();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                unitOfWork.ExecuteInTransactionAsync<bool>(async ct =>
                {
                    await users.ReplaceRolesAsync(userId, [Roles.Nurse, Roles.Nurse], ct);
                    await unitOfWork.SaveChangesAsync(ct);
                    return true;
                }));
        }

        // The rollback restored the original assignment: exactly one row, the
        // original role — not zero rows, not a partial replacement.
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
            var roles = await db.UserRoles.Where(r => r.UserId == userId).ToListAsync();
            var role = Assert.Single(roles);
            Assert.Equal(Roles.Doctor, role.Role);
        }
    }
}
