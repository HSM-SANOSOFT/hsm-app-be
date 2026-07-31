using System.Text.Json;

namespace Hsm.Contract.Tests;

/// <summary>
/// Base for contract tests over a booted host: schema readiness, a
/// cookie-transparent client, seed/login helpers, and the frozen-envelope
/// assertions. Carries the Infra trait — these tests need live PostgreSQL
/// (and, per module, RustFS), so CI's unit job filters them out with
/// Infra!=true.
/// </summary>
[Trait("Infra", "true")]
public abstract class ContractTest<TFactory>(TFactory factory) : IAsyncLifetime
    where TFactory : IContractHost
{
    protected TFactory Factory { get; } = factory;

    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Factory.EnsureSchemaAsync();
        Client = Factory.CreateApiClient();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    protected static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    /// <summary>Seeds a user with <paramref name="role"/>, logs it in, and
    /// returns the bearer access token with the user id.</summary>
    protected async Task<(string Bearer, Guid UserId)> BearerAsync(
        string role = "doctor", bool onboarded = true)
    {
        var username = Unique("user");
        const string password = "Contract-Passw0rd";
        var id = await Factory.SeedUserAsync(
            username, password, role, onboarded ? DateTimeOffset.UtcNow : null);
        var login = await Api.PostJsonAsync(Client, "/v1/auth/login", new { username, password });
        Assert.True(login.Status == 201, $"login failed: {login.RawBody}");
        return (login.AccessToken, id);
    }

    /// <summary>Pins the frozen success-envelope metadata.</summary>
    protected static void AssertSuccessEnvelope(ApiResponse response, int statusCode, string path) =>
        EnvelopeAssert.Success(response, statusCode, path);

    /// <summary>Pins the frozen error-envelope metadata and stable issue.code.</summary>
    protected static JsonElement AssertErrorEnvelope(ApiResponse response, int statusCode, string expectedCode) =>
        EnvelopeAssert.Error(response, statusCode, expectedCode);

    /// <summary>Asserts a COMMON.VALIDATION 400 carrying a constraint for a field.</summary>
    protected static void AssertValidationFailure(ApiResponse response, string field, string constraint) =>
        EnvelopeAssert.Validation(response, field, constraint);
}
