using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// The frozen environment gate: developer-role principals can only be issued
/// tokens in the dev environment (auth.service.ts generateTokens).
/// </summary>
public sealed class DeveloperGateContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Developer_login_works_in_dev()
    {
        var username = Unique("dev");
        await Factory.SeedUserAsync(username, "Dev-Passw0rd", "developer", DateTimeOffset.UtcNow);

        var response = await LoginAsync(username, "Dev-Passw0rd");

        AssertSuccessEnvelope(response, 201, "/v1/auth/login");
    }

    [Fact]
    public async Task Developer_login_is_forbidden_outside_dev()
    {
        var username = Unique("prod_dev");
        await Factory.SeedUserAsync(username, "Dev-Passw0rd", "developer", DateTimeOffset.UtcNow);

        using var prodFactory = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:Environment", "prod"));
        using var client = prodFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var response = await Api.PostJsonAsync(
            client, "/v1/auth/login", new { username, password = "Dev-Passw0rd" });

        var issue = AssertErrorEnvelope(response, 403, "COMMON.FORBIDDEN");
        Assert.Equal(
            "Developer role cannot be assigned in this environment",
            issue.GetProperty("message").GetString());
    }
}
