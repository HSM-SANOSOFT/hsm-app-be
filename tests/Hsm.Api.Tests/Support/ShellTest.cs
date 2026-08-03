namespace Hsm.Api.Tests;

/// <summary>
/// Base for the three shell suites carried over from the retired
/// frozen-contract tests: schema readiness, a cookie-transparent client, and
/// seed/login helpers built on the pre-Identity POST /v1/auth/login. Task 12
/// rewrites <c>Shell/*Tests.cs</c> onto the Identity cookie mechanism and this
/// file retires with them.
/// </summary>
public abstract class ShellTest<TFactory>(TFactory factory) : IAsyncLifetime
    where TFactory : ShellFactory
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
}
