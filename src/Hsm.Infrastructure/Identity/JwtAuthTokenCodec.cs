using System.Text;
using System.Text.Json;
using Hsm.Application.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hsm.Infrastructure.Identity;

/// <summary>Secrets for the two frozen JWT families (JWT_AT/RT_SECRET).</summary>
public sealed class AuthTokenOptions
{
    public string AccessSecret { get; set; } = string.Empty;
    public string RefreshSecret { get; set; } = string.Empty;
}

/// <summary>
/// HS256 JWTs with the frozen claim layout: sub + id (both), username, email,
/// firstName, firstLastName, roles[], onboardingCompletedAt for users;
/// sub + id, name, roles[] for integrations.
/// </summary>
public sealed class JwtAuthTokenCodec(AuthTokenOptions options) : IAuthTokenCodec
{
    private static readonly JsonWebTokenHandler Handler = new();

    public string Sign(AuthPrincipal principal, TokenKind kind, TimeSpan lifetime)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = principal.Id,
            ["id"] = principal.Id,
            ["roles"] = principal.Roles.ToArray(),
            // Uniqueness claim (not in the frozen payload): guarantees two
            // tokens minted in the same second still differ, so refresh
            // rotation ALWAYS revokes the prior token's hash.
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        if (principal.Username is not null)
        {
            claims["username"] = principal.Username;
        }

        if (principal.Email is not null)
        {
            claims["email"] = principal.Email;
        }

        if (principal.FirstName is not null)
        {
            claims["firstName"] = principal.FirstName;
        }

        if (principal.FirstLastName is not null)
        {
            claims["firstLastName"] = principal.FirstLastName;
        }

        if (principal.Name is not null)
        {
            claims["name"] = principal.Name;
        }

        if (principal.HasOnboardingClaim)
        {
            // Serializes as an explicit null while pending, as the frozen
            // payload did.
            claims["onboardingCompletedAt"] = (object?)principal.OnboardingCompletedAt!;
        }

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            SigningCredentials = new SigningCredentials(KeyFor(kind), SecurityAlgorithms.HmacSha256),
        };
        return Handler.CreateToken(descriptor);
    }

    public async Task<TokenValidation> ValidateAsync(string token, TokenKind kind, bool ignoreExpiration = false)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = !ignoreExpiration,
            IssuerSigningKey = KeyFor(kind),
            // The frozen verifier had no clock tolerance.
            ClockSkew = TimeSpan.Zero,
        };

        var result = await Handler.ValidateTokenAsync(token, parameters).ConfigureAwait(false);
        if (!result.IsValid)
        {
            return result.Exception is SecurityTokenExpiredException
                ? TokenValidation.Expired
                : TokenValidation.Invalid;
        }

        var jwt = (JsonWebToken)result.SecurityToken;
        return new TokenValidation(ToPrincipal(jwt), IsExpired: false);
    }

    private static AuthPrincipal ToPrincipal(JsonWebToken jwt)
    {
        var hasOnboarding = jwt.TryGetPayloadValue<object?>("onboardingCompletedAt", out _)
            || PayloadHasNullOnboarding(jwt);
        return new AuthPrincipal
        {
            Id = jwt.Subject,
            Username = TryGetString(jwt, "username"),
            Email = TryGetString(jwt, "email"),
            FirstName = TryGetString(jwt, "firstName"),
            FirstLastName = TryGetString(jwt, "firstLastName"),
            Name = TryGetString(jwt, "name"),
            Roles = jwt.TryGetPayloadValue<string[]>("roles", out var roles) ? roles : [],
            OnboardingCompletedAt = TryGetString(jwt, "onboardingCompletedAt"),
            HasOnboardingClaim = hasOnboarding,
            IssuedAt = new DateTimeOffset(jwt.IssuedAt, TimeSpan.Zero).ToUnixTimeSeconds(),
            ExpiresAt = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero).ToUnixTimeSeconds(),
        };
    }

    private static string? TryGetString(JsonWebToken jwt, string claim) =>
        jwt.TryGetPayloadValue<string>(claim, out var value) ? value : null;

    /// <summary>
    /// A pending user's token carries `"onboardingCompletedAt": null`; typed
    /// TryGetPayloadValue can miss JSON null, so check the raw payload.
    /// </summary>
    private static bool PayloadHasNullOnboarding(JsonWebToken jwt)
    {
        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(jwt.EncodedPayload));
        return payload.RootElement.TryGetProperty("onboardingCompletedAt", out _);
    }

    private SymmetricSecurityKey KeyFor(TokenKind kind) =>
        new(Encoding.UTF8.GetBytes(kind == TokenKind.Access ? options.AccessSecret : options.RefreshSecret));
}
