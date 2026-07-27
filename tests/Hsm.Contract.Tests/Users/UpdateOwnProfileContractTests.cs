using Hsm.Contract.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Users;

/// <summary>
/// PATCH /v1/user/me — behavior pinned from the frozen user.controller /
/// users.service updateOwnProfile at freeze/typescript-2026-07-27: only
/// firstName and email are reachable; the role cannot be touched (R6).
/// </summary>
public sealed class UpdateOwnProfileContractTests(UsersApiFactory factory)
    : UsersContractTest(factory), IClassFixture<UsersApiFactory>
{
    private static readonly string[] AdminRoles = ["admin"];

    [Fact]
    public async Task Own_firstName_and_email_update_and_the_user_with_roles_is_returned()
    {
        var (username, password, _, id) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);
        var newEmail = $"{Unique("changed")}@contract.test";

        var response = await Api.PatchJsonAsync(
            Client, "/v1/user/me", new { firstName = "Renamed", email = newEmail }, bearer: login.AccessToken);

        AssertSuccessEnvelope(response, 200, "/v1/user/me");
        var data = response.Data;
        Assert.Equal(id.ToString(), data.GetProperty("id").GetString());
        Assert.Equal("Renamed", data.GetProperty("firstName").GetString());
        Assert.Equal(newEmail, data.GetProperty("email").GetString());
        Assert.Equal(username, data.GetProperty("username").GetString());
        // The password hash never rides in a response body.
        Assert.False(data.TryGetProperty("password", out _));
        Assert.False(data.TryGetProperty("passwordHash", out _));
        // Roles ride along (frozen findUserById relations) and are UNCHANGED.
        var role = Assert.Single(data.GetProperty("roles").EnumerateArray());
        Assert.Equal("patient", role.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Empty_body_is_a_no_op_returning_the_current_profile()
    {
        var (username, password, email, id) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PatchJsonAsync(Client, "/v1/user/me", new { }, bearer: login.AccessToken);

        AssertSuccessEnvelope(response, 200, "/v1/user/me");
        Assert.Equal(id.ToString(), response.Data.GetProperty("id").GetString());
        Assert.Equal(email, response.Data.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Role_fields_are_not_reachable_through_own_profile_update()
    {
        // The frozen DTO whitelists firstName/email only; a role (or roles)
        // property is rejected outright — self-escalation is impossible here.
        var (username, password, _, id) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var withRole = await Api.PatchJsonAsync(
            Client, "/v1/user/me", new { role = "admin" }, bearer: login.AccessToken);
        var withRoles = await Api.PatchJsonAsync(
            Client, "/v1/user/me", new { roles = AdminRoles }, bearer: login.AccessToken);

        AssertValidationFailure(withRole, "role", "whitelistValidation");
        AssertValidationFailure(withRoles, "roles", "whitelistValidation");

        // And the stored role rows are untouched.
        var roles = await Factory.WithDbAsync(db =>
            db.UserRoles.Where(r => r.UserId == id).ToListAsync());
        Assert.Equal("patient", Assert.Single(roles).Role);
    }

    [Fact]
    public async Task Provided_but_empty_fields_fail_validation()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PatchJsonAsync(
            Client, "/v1/user/me", new { firstName = "" }, bearer: login.AccessToken);

        AssertValidationFailure(response, "firstName", "isNotEmpty");
    }

    [Fact]
    public async Task Unauthenticated_request_is_401()
    {
        var response = await Api.PatchJsonAsync(Client, "/v1/user/me", new { firstName = "X" });
        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Pending_onboarding_staff_is_blocked_with_403()
    {
        // No @AllowPending on the frozen route — the global OnboardingGuard
        // applies (admins exempt; this staff user is not one).
        var (username, password, _, _) = await SeedStaffAsync("doctor", pendingOnboarding: true);
        var login = await LoginAsync(username, password);

        var response = await Api.PatchJsonAsync(
            Client, "/v1/user/me", new { firstName = "X" }, bearer: login.AccessToken);

        AssertErrorEnvelope(response, 403, "COMMON.FORBIDDEN");
    }

    [Fact]
    public async Task Cookie_authenticated_mutation_without_csrf_is_403()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PatchJsonAsync(
            Client, "/v1/user/me", new { firstName = "X" },
            cookies: [("access_token", login.AccessToken)]);

        // The frozen rejection surfaced from the middleware layer, outside
        // the envelope.
        Assert.Equal(403, response.Status);
        Assert.Contains("invalid csrf token", response.RawBody, StringComparison.OrdinalIgnoreCase);
    }
}
