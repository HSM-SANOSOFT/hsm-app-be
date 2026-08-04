using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

/// <summary>
/// The membership schema, proven against real PostgreSQL: the role catalog is
/// seeded by the MIGRATION (not by a startup task two hosts could race),
/// passwords are hashed by Identity's PBKDF2 rather than bcrypt, and the
/// username uniqueness the frozen citext column gave us survives the move to
/// Identity's normalized columns.
/// </summary>
public class IdentitySchemaTests
{
    [Fact]
    public async Task Every_catalog_role_is_seeded_as_an_identity_role()
    {
        using var provider = TestServices.Build();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        await db.Database.MigrateAsync(CancellationToken.None);

        var seeded = await db.Roles.Select(r => r.Name).ToListAsync(CancellationToken.None);

        Assert.Equal(
            RoleCatalog.All.OrderBy(r => r, StringComparer.Ordinal),
            seeded.OrderBy(r => r, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_user_created_through_UserManager_hashes_with_PBKDF2_and_authenticates()
    {
        using var provider = TestServices.Build();
        await TestServices.MigrateAsync(provider);
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var user = new HsmUser
        {
            UserName = $"u{Guid.NewGuid():N}"[..20],
            Email = $"{Guid.NewGuid():N}@schema.test",
            FirstName = "Ada",
            FirstLastName = "Lovelace",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var created = await users.CreateAsync(user, "Schema-Passw0rd");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        // ASP.NET Core Identity's v3 PBKDF2 format is a base64 blob whose first
        // byte is the format marker 0x01. bcrypt hashes start "$2".
        Assert.StartsWith("AQ", user.PasswordHash, StringComparison.Ordinal);
        Assert.True(await users.CheckPasswordAsync(user, "Schema-Passw0rd"));
        Assert.False(await users.CheckPasswordAsync(user, "wrong"));
    }

    [Fact]
    public async Task Usernames_stay_case_insensitively_unique()
    {
        using var provider = TestServices.Build();
        await TestServices.MigrateAsync(provider);
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var name = $"u{Guid.NewGuid():N}"[..20];

        var first = await users.CreateAsync(NewUser(name), "Schema-Passw0rd");
        Assert.True(first.Succeeded);

        var second = await users.CreateAsync(NewUser(name.ToUpperInvariant()), "Schema-Passw0rd");

        Assert.False(second.Succeeded);
    }

    private static HsmUser NewUser(string name) => new()
    {
        UserName = name,
        Email = $"{Guid.NewGuid():N}@schema.test",
        FirstName = "A",
        FirstLastName = "B",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
