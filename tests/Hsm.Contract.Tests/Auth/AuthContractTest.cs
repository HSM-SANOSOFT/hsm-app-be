namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// Base for auth contract tests: the shared plumbing plus patient/admin seed
/// helpers and the login shorthand.
/// </summary>
public abstract class AuthContractTest(AuthApiFactory factory) : ContractTest<AuthApiFactory>(factory)
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

    protected Task<ApiResponse> LoginAsync(string username, string password) =>
        Api.PostJsonAsync(Client, "/v1/auth/login", new { username, password });
}
