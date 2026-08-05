using System.Text;
using System.Text.Json;
using Hsm.Application.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// The signing key for integration access tokens (<c>Auth:JwtAccessSecret</c>).
/// It is also the key <c>AddJwtBearer</c> verifies with, so there is exactly one
/// secret in this system and no way for signer and verifier to disagree.
/// </summary>
public sealed class IntegrationTokenOptions
{
    public string AccessSecret { get; set; } = string.Empty;
}

/// <summary>
/// HS256 access tokens for integration accounts: sub + id, name, roles[].
///
/// <para>The refresh half of this type is gone. It used to sign a second JWT
/// with a second secret, which meant a refresh token was a readable document
/// asserting claims that only its own re-signing ever consumed. Refresh tokens
/// are opaque now (<see cref="IntegrationTokenIssuer"/>), so the second key,
/// the second validation-parameter pair and the whole notion of a token "kind"
/// went with them.</para>
/// </summary>
public sealed class IntegrationTokenCodec : IIntegrationTokenCodec
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly SigningCredentials _credentials;
    private readonly TokenValidationParameters _parameters;
    private readonly TokenValidationParameters _parametersNoLifetime;

    public IntegrationTokenCodec(IntegrationTokenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The options are a singleton — the key, the credentials and both
        // validation-parameter variants are built once, not per call.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.AccessSecret));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _parameters = ParametersFor(key, validateLifetime: true);
        _parametersNoLifetime = ParametersFor(key, validateLifetime: false);
    }

    public string Sign(AuthPrincipal principal, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = principal.Id,
            ["id"] = principal.Id,
            ["roles"] = principal.Roles.ToArray(),
            // Uniqueness claim: guarantees two tokens minted in the same second
            // still differ, so no caller can mistake a re-issue for a replay.
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
            // Serializes as an explicit null while pending.
            claims["onboardingCompletedAt"] = (object?)principal.OnboardingCompletedAt!;
        }

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            SigningCredentials = _credentials,
        };
        return Handler.CreateToken(descriptor);
    }

    public async Task<TokenValidation> ValidateAsync(string token, bool ignoreExpiration = false)
    {
        var parameters = ignoreExpiration ? _parametersNoLifetime : _parameters;

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

    private static TokenValidationParameters ParametersFor(SymmetricSecurityKey key, bool validateLifetime) => new()
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = validateLifetime,
        IssuerSigningKey = key,
        // No clock tolerance: a default five-minute skew is five extra minutes
        // of life for a revoked integration token.
        ClockSkew = TimeSpan.Zero,
    };
}
