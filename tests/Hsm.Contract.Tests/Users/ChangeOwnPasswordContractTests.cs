
namespace Hsm.Contract.Tests.Users;

/// <summary>
/// POST /v1/user/me/password — behavior pinned from the frozen
/// users.service changeOwnPassword: the current password is verified before
/// the change, and a wrong current password fails WITHOUT touching the
/// stored password or the active session.
/// </summary>
public sealed class ChangeOwnPasswordContractTests(UsersApiFactory factory)
    : UsersContractTest(factory), IClassFixture<UsersApiFactory>
{
    [Fact]
    public async Task Correct_current_password_changes_the_password()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client, "/v1/user/me/password",
            new { currentPassword = password, newPassword = "Brand-New-Passw0rd" },
            bearer: login.AccessToken);

        // Frozen changeOwnPassword returned void — 201 (NestJS POST default)
        // with no data key.
        AssertSuccessEnvelope(response, 201, "/v1/user/me/password");
        Assert.False(response.HasData);

        // The new password authenticates; the old one no longer does.
        AssertSuccessEnvelope(await LoginAsync(username, "Brand-New-Passw0rd"), 201, "/v1/auth/login");
        AssertErrorEnvelope(await LoginAsync(username, password), 401, "AUTH.INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Wrong_current_password_fails_without_invalidating_the_session()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client, "/v1/user/me/password",
            new { currentPassword = "not-the-password", newPassword = "Brand-New-Passw0rd" },
            bearer: login.AccessToken);

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Current password is incorrect", issue.GetProperty("message").GetString());

        // The session survives the failed attempt: the same access token still
        // authenticates and the refresh token still rotates.
        var profile = await Api.GetAsync(Client, "/v1/auth/profile", bearer: login.AccessToken);
        AssertSuccessEnvelope(profile, 200, "/v1/auth/profile");
        var refresh = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: login.RefreshToken);
        AssertSuccessEnvelope(refresh, 200, "/v1/auth/refresh");

        // And the stored password is unchanged.
        AssertSuccessEnvelope(await LoginAsync(username, password), 201, "/v1/auth/login");
    }

    [Fact]
    public async Task Short_new_password_fails_validation()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client, "/v1/user/me/password",
            new { currentPassword = password, newPassword = "short" },
            bearer: login.AccessToken);

        AssertValidationFailure(response, "newPassword", "minLength");
    }

    [Fact]
    public async Task Missing_current_password_fails_validation()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client, "/v1/user/me/password",
            new { newPassword = "Brand-New-Passw0rd" },
            bearer: login.AccessToken);

        AssertValidationFailure(response, "currentPassword", "isNotEmpty");
    }

    [Fact]
    public async Task Unauthenticated_request_is_401()
    {
        var response = await Api.PostJsonAsync(
            Client, "/v1/user/me/password",
            new { currentPassword = "a-password", newPassword = "Brand-New-Passw0rd" });
        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }
}
