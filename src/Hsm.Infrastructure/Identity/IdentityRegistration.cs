using System.Text;
using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// Identity's core services: UserManager, RoleManager, the token providers used
/// by password reset, and the EF stores — plus, for the two HTTP hosts,
/// <see cref="AddHsmIdentityAuthentication"/>: the cookie/bearer schemes and
/// the antiforgery posture. <see cref="AddHsmIdentity"/> alone stays usable
/// from a bare <c>ServiceCollection</c> (the integration tests) because
/// nothing in it needs an <c>HttpContext</c>.
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

        services.AddScoped<IUserDirectory, UserDirectory>();
        return services;
    }

    /// <summary>
    /// Two authentication handlers behind one adaptive scheme: browsers carry
    /// the Identity application cookie, integrations carry a JWT. The selector
    /// reproduces the cookie-then-bearer resolution the hand-rolled RequestAuth
    /// did, with framework middleware instead — and the ORDER matters: a caller
    /// who sent a bearer token gets the bearer handler's answer, so a stale
    /// cookie can never silently rescue a bad token.
    ///
    /// <para>Both hosts call this, which is what makes one sign-in serve both
    /// doors: the cookie's name, path, SameSite mode and lifetime are stated
    /// once here, where <c>AuthCookiePolicy</c> used to state them for two
    /// hand-rolled writers. Each host still owns its own cookie EVENTS — a 401
    /// and a redirect to a sign-in page are both correct answers to the same
    /// situation on different doors.</para>
    /// </summary>
    public static IServiceCollection AddHsmIdentityAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var cookieSecure = configuration.GetValue("Auth:CookieSecure", defaultValue: false);

        // Read at COMPOSITION time, not inside AddJwtBearer's lazy callback: a
        // deployment that forgot the secret should fail to boot, not fail on
        // the first integration call with a 500 nobody is watching for.
        var accessSecret = configuration["Auth:JwtAccessSecret"];
        if (string.IsNullOrEmpty(accessSecret))
        {
            throw new InvalidOperationException(
                "Auth:JwtAccessSecret is required — it is the key integration bearer tokens are verified with.");
        }

        var authentication = services.AddAuthentication(HsmAuthenticationSchemes.Adaptive);

        authentication.AddPolicyScheme(
            HsmAuthenticationSchemes.Adaptive,
            displayName: "Cookie or bearer",
            options => options.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString()
                    .StartsWith("Bearer ", StringComparison.Ordinal)
                        ? JwtBearerDefaults.AuthenticationScheme
                        : IdentityConstants.ApplicationScheme);

        authentication.AddIdentityCookies();

        authentication.AddJwtBearer(options =>
        {
            // Inbound claim MAPPING is off on purpose. The JWT layout is
            // IAuthTokenCodec's and is contract (sub/id/roles/username/…);
            // with the map on, whether "roles" reaches ClaimTypes.Role depends
            // on a framework lookup table rather than on anything stated here,
            // and an integration whose roles silently vanish authenticates
            // successfully and then 403s on every route. Off, the claims are
            // exactly what was signed and the two type names below say so.
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(accessSecret)),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                // The frozen verifier had no clock tolerance and neither does
                // this one: a five-minute default skew is five extra minutes of
                // life for a revoked integration token.
                ClockSkew = TimeSpan.Zero,
                RoleClaimType = "roles",
                NameClaimType = "username",
            };
        });

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "hsm.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy =
                cookieSecure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        services.AddScoped<IUserClaimsPrincipalFactory<HsmUser>, HsmUserClaimsPrincipalFactory>();

        // SignInManager is the only thing that writes the session cookie, and
        // it needs the schemes above — which is why it registers here rather
        // than in AddHsmIdentity. ISecurityStampValidator comes with it, and
        // the application cookie's OnValidatePrincipal resolves that on every
        // request: without this call every cookie-authenticated request throws.
        services.AddScoped<SignInManager<HsmUser>>();
        services.AddScoped<ISecurityStampValidator, SecurityStampValidator<HsmUser>>();
        services.AddScoped<ITwoFactorSecurityStampValidator, TwoFactorSecurityStampValidator<HsmUser>>();
        services.AddHttpContextAccessor();

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "hsm.antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy =
                cookieSecure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }
}
