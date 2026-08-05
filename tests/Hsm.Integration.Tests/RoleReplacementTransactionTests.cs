using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

/// <summary>
/// Proves the role-replacement transactional boundary (plan U13 / DoD C2)
/// against real PostgreSQL: the frozen changeUserRole ran delete-then-insert
/// inside ONE transaction, so an induced failure after the delete must leave
/// the original role rows intact — never a user stripped of roles.
///
/// <para><b>Why this suite was rewritten and not merely retyped (Task 11).</b>
/// The delete half is no longer our own <c>ExecuteDeleteAsync</c>; it is
/// <see cref="UserManager{TUser}.RemoveFromRolesAsync"/>, and Identity's EF
/// store runs with <c>AutoSaveChanges = true</c> — it calls
/// <c>SaveChangesAsync</c> ITSELF after every mutating call. If that inner save
/// committed independently, an exception between the removal and the
/// re-assignment would leave an account with no roles at all and nothing would
/// notice. It does not, and this suite is the proof: Identity's store resolves
/// the same scoped <see cref="HsmDbContext"/> the pipeline opened its
/// transaction on, so its saves are writes INSIDE that transaction and roll
/// back with it. Both a database-level failure and a plain application
/// exception are induced, because they leave SaveChanges by different
/// paths.</para>
/// </summary>
public sealed class RoleReplacementTransactionTests : IAsyncLifetime
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
                .Append("Database=hsm_roles_test"));
        _provider = TestServices.Build(builder =>
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HsmDb"] = dedicated,
            }));

        await TestServices.RecreateAsync(_provider);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task A_database_failure_after_the_removal_rolls_back_to_the_original_assignment()
    {
        var userId = await SeedDoctorAsync();

        // Induce a failure INSIDE the boundary, at the database: after the
        // removal has already executed (and Identity's store has already called
        // SaveChanges on it), insert an assignment naming a role that does not
        // exist, which the user_roles → roles foreign key refuses.
        using (var scope = _provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
            var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                unitOfWork.ExecuteInTransactionAsync<bool>(
                    async ct =>
                    {
                        var user = await users.FindByIdAsync(userId.ToString());
                        var held = await users.GetRolesAsync(user!);
                        var removed = await users.RemoveFromRolesAsync(user!, held);
                        Assert.True(removed.Succeeded);

                        // The removal really did reach the database inside this
                        // transaction — so what the assertion below observes is
                        // a ROLLBACK, not a write that never happened.
                        Assert.Empty(await users.GetRolesAsync(user!));

                        db.UserRoles.Add(new IdentityUserRole<Guid>
                        {
                            UserId = userId,
                            RoleId = Guid.NewGuid(),
                        });
                        await db.SaveChangesAsync(ct);
                        return true;
                    },
                    CancellationToken.None));
        }

        await AssertStillOnlyDoctorAsync(userId);
    }

    [Fact]
    public async Task An_application_exception_after_the_removal_rolls_back_to_the_original_assignment()
    {
        var userId = await SeedDoctorAsync();

        // The same boundary, failing the other way: the removal succeeds and
        // the re-assignment never runs. This is the shape a validation refusal
        // or a cancelled request takes, and it is the one that would silently
        // strip an account of every role if Identity's inner SaveChanges
        // committed on its own.
        using (var scope = _provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                unitOfWork.ExecuteInTransactionAsync<bool>(
                    async _ =>
                    {
                        var user = await users.FindByIdAsync(userId.ToString());
                        var removed = await users.RemoveFromRolesAsync(
                            user!, await users.GetRolesAsync(user!));
                        Assert.True(removed.Succeeded);
                        throw new InvalidOperationException("induced after the removal");
                    },
                    CancellationToken.None));
        }

        await AssertStillOnlyDoctorAsync(userId);
    }

    [Fact]
    public async Task Re_assigning_a_held_role_succeeds_because_the_removal_runs_first()
    {
        // The reason ChangeUserRoleHandler removes before it adds: the
        // (user, role) primary key would refuse the insert otherwise, and
        // "change a doctor to a doctor" is a request an admin can make.
        var userId = await SeedDoctorAsync();

        using (var scope = _provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await unitOfWork.ExecuteInTransactionAsync(
                async _ =>
                {
                    var user = await users.FindByIdAsync(userId.ToString());
                    await users.RemoveFromRolesAsync(user!, await users.GetRolesAsync(user!));
                    var added = await users.AddToRoleAsync(user!, Roles.Doctor);
                    Assert.True(added.Succeeded, string.Join("; ", added.Errors.Select(e => e.Description)));
                    return true;
                },
                CancellationToken.None);
        }

        await AssertStillOnlyDoctorAsync(userId);
    }

    private async Task<Guid> SeedDoctorAsync()
    {
        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var user = new HsmUser
        {
            UserName = $"txn_{Guid.NewGuid():N}",
            Email = $"txn_{Guid.NewGuid():N}@integration.test",
            FirstName = "Txn",
            FirstLastName = "Proof",
            OnboardingCompletedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, "Txn-Passw0rd");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        var assigned = await users.AddToRoleAsync(user, Roles.Doctor);
        Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(e => e.Description)));
        return user.Id;
    }

    /// <summary>
    /// Exactly one assignment, the original role — not zero rows, not a partial
    /// replacement. Read on a FRESH scope so nothing can be answered out of the
    /// change tracker of the context that did the writing.
    /// </summary>
    private async Task AssertStillOnlyDoctorAsync(Guid userId)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var roles = await (
            from assignment in db.UserRoles
            join role in db.Roles on assignment.RoleId equals role.Id
            where assignment.UserId == userId
            select role.Name).ToListAsync(CancellationToken.None);

        Assert.Equal(Roles.Doctor, Assert.Single(roles));
    }
}
