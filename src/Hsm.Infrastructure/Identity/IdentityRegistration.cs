using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// Identity's core services: UserManager, RoleManager, the token providers used
/// by password reset, and the EF stores. SignInManager and the authentication
/// SCHEMES are not here — they need HttpContext and belong to a host (Task 12).
/// </summary>
public static class IdentityRegistration
{
    public static IServiceCollection AddHsmIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The reset-token provider protects its payload with the data-protection
        // stack. A web host wires that up itself; a bare ServiceCollection (the
        // integration tests) does not, and the failure would be a resolution
        // error far from its cause. AddDataProtection uses TryAdd, so a host's
        // own configuration still wins.
        services.AddDataProtection();

        services
            .AddIdentityCore<HsmUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Deliberately modest: length is the only requirement that
                // measurably helps, and character-class rules push users toward
                // predictable substitutions. Identity's default hasher (PBKDF2,
                // v3 format) is left alone.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HsmDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<Application.Auth.IUserDirectory, UserDirectory>();
        return services;
    }
}
