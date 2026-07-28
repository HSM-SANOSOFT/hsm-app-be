namespace Hsm.Contract.Tests.Users;

/// <summary>
/// Base for the U13 user/settings contract tests: the shared plumbing plus
/// seed helpers and the JWT-claim shorthand.
/// </summary>
public abstract class UsersContractTest(UsersApiFactory factory) : ContractTest<UsersApiFactory>(factory)
{
    protected async Task<(string Username, string Password, string Email, Guid Id)> SeedPatientAsync()
    {
        var username = Unique("patient");
        var password = "Patient-Passw0rd";
        var id = await Factory.SeedUserAsync(username, password, "patient", DateTimeOffset.UtcNow);
        return (username, password, $"{username}@contract.test", id);
    }

    protected async Task<(string Username, string Password, string Email, Guid Id)> SeedAdminAsync()
    {
        var username = Unique("admin");
        var password = "Admin-Passw0rd";
        var id = await Factory.SeedUserAsync(username, password, "admin", DateTimeOffset.UtcNow);
        return (username, password, $"{username}@contract.test", id);
    }

    protected async Task<(string Username, string Password, string Email, Guid Id)> SeedStaffAsync(
        string role, bool pendingOnboarding = false)
    {
        var username = Unique("staff");
        var password = "Staff-Passw0rd";
        var id = await Factory.SeedUserAsync(
            username, password, role, pendingOnboarding ? null : DateTimeOffset.UtcNow);
        return (username, password, $"{username}@contract.test", id);
    }

    protected Task<ApiResponse> LoginAsync(string username, string password) =>
        Api.PostJsonAsync(Client, "/v1/auth/login", new { username, password });

    /// <summary>Seeds an admin, logs it in, and returns a bearer access token.</summary>
    protected async Task<(string Bearer, Guid AdminId)> AdminBearerAsync()
    {
        var (username, password, _, id) = await SeedAdminAsync();
        var login = await LoginAsync(username, password);
        return (login.AccessToken, id);
    }

    /// <summary>Decodes the roles claim of a JWT access token.</summary>
    protected static IReadOnlyList<string> RolesOf(string accessToken) =>
        [.. Api.DecodeJwtPayload(accessToken).GetProperty("roles").EnumerateArray().Select(r => r.GetString()!)];
}
