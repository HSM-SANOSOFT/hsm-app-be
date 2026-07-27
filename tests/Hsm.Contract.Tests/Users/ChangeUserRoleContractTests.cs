using Hsm.Contract.Tests.Auth;

namespace Hsm.Contract.Tests.Users;

/// <summary>
/// PATCH /v1/user/{id}/role — admin-only role replacement, pinned from the
/// frozen users.service changeUserRole (delete-then-insert inside one
/// transaction) and the frozen RolesGuard's JWT-claims authorization.
/// </summary>
public sealed class ChangeUserRoleContractTests(UsersApiFactory factory)
    : UsersContractTest(factory), IClassFixture<UsersApiFactory>
{
    [Fact]
    public async Task Admin_replaces_the_role_and_the_database_reflects_it_immediately()
    {
        var (bearer, _) = await AdminBearerAsync();
        var (_, _, _, id) = await SeedPatientAsync();

        var response = await Api.PatchJsonAsync(
            Client, $"/v1/user/{id}/role", new { role = "doctor" }, bearer: bearer);

        AssertSuccessEnvelope(response, 200, $"/v1/user/{id}/role");
        var role = Assert.Single(response.Data.GetProperty("roles").EnumerateArray());
        Assert.Equal("doctor", role.GetProperty("role").GetString());
        Assert.Equal("Clinical", role.GetProperty("domain").GetString());

        // The database row is authoritative immediately — a fresh read shows
        // the new assignment with no waiting period.
        var fetched = await Api.GetAsync(Client, $"/v1/user/{id}", bearer: bearer);
        var fetchedRole = Assert.Single(fetched.Data.GetProperty("roles").EnumerateArray());
        Assert.Equal("doctor", fetchedRole.GetProperty("role").GetString());
    }

    [Fact]
    public async Task New_roles_take_effect_on_the_next_login()
    {
        var (bearer, _) = await AdminBearerAsync();
        var (username, password, _, id) = await SeedPatientAsync();

        // Before the change the user cannot reach an admin route.
        var before = await LoginAsync(username, password);
        AssertErrorEnvelope(await Api.GetAsync(Client, "/v1/user", bearer: before.AccessToken), 403, "COMMON.FORBIDDEN");

        var change = await Api.PatchJsonAsync(
            Client, $"/v1/user/{id}/role", new { role = "admin" }, bearer: bearer);
        AssertSuccessEnvelope(change, 200, $"/v1/user/{id}/role");

        // The next authenticated session carries the new role: credentials are
        // re-validated against the database at login, so the fresh token both
        // claims and exercises admin.
        var after = await LoginAsync(username, password);
        Assert.Equal("admin", Assert.Single(RolesOf(after.AccessToken)));
        AssertSuccessEnvelope(await Api.GetAsync(Client, "/v1/user", bearer: after.AccessToken), 200, "/v1/user");
    }

    [Fact]
    public async Task Pinned_frozen_behavior_a_live_access_token_keeps_its_old_roles_until_expiry()
    {
        // FROZEN SEMANTICS, deliberately preserved: authorization is decided
        // from the JWT's own roles claim (frozen RolesGuard) and changeUserRole
        // revoked nothing. A demoted admin's outstanding access token therefore
        // KEEPS admin power until it expires (≤15 min). There is no server-side
        // authorization cache — the only "cached" state is the client-held JWT.
        var (adminBearer, _) = await AdminBearerAsync();
        var (username, password, _, id) = await SeedAdminAsync();
        var victim = await LoginAsync(username, password);

        var change = await Api.PatchJsonAsync(
            Client, $"/v1/user/{id}/role", new { role = "patient" }, bearer: adminBearer);
        AssertSuccessEnvelope(change, 200, $"/v1/user/{id}/role");

        // The pre-change token still authorizes admin routes (stale claims).
        var withStaleToken = await Api.GetAsync(Client, "/v1/user", bearer: victim.AccessToken);
        AssertSuccessEnvelope(withStaleToken, 200, "/v1/user");

        // A fresh login reflects the demotion.
        var fresh = await LoginAsync(username, password);
        Assert.Equal("patient", Assert.Single(RolesOf(fresh.AccessToken)));
        AssertErrorEnvelope(await Api.GetAsync(Client, "/v1/user", bearer: fresh.AccessToken), 403, "COMMON.FORBIDDEN");
    }

    [Fact]
    public async Task Pinned_frozen_behavior_refresh_reissues_the_old_roles_from_the_token_claims()
    {
        // FROZEN SEMANTICS: /v1/auth/refresh rebuilds the pair from the refresh
        // token's OWN claims (validateRefreshToken stripped iat/exp and re-signed
        // the rest) — it does not re-read roles from the database. A demoted
        // user's refresh therefore perpetuates the old roles until re-login.
        var (adminBearer, _) = await AdminBearerAsync();
        var (username, password, _, id) = await SeedAdminAsync();
        var victim = await LoginAsync(username, password);

        var change = await Api.PatchJsonAsync(
            Client, $"/v1/user/{id}/role", new { role = "patient" }, bearer: adminBearer);
        AssertSuccessEnvelope(change, 200, $"/v1/user/{id}/role");

        var refreshed = await Api.GetAsync(Client, "/v1/auth/refresh", bearer: victim.RefreshToken);
        AssertSuccessEnvelope(refreshed, 200, "/v1/auth/refresh");
        Assert.Equal("admin", Assert.Single(RolesOf(refreshed.AccessToken)));
    }

    [Fact]
    public async Task Reassigning_the_currently_held_role_succeeds()
    {
        // Frozen order pinned: delete-then-insert, so re-assigning the held
        // role never trips the (user, domain, role) unique index.
        var (bearer, _) = await AdminBearerAsync();
        var (_, _, _, id) = await SeedStaffAsync("doctor");

        var response = await Api.PatchJsonAsync(
            Client, $"/v1/user/{id}/role", new { role = "doctor" }, bearer: bearer);

        AssertSuccessEnvelope(response, 200, $"/v1/user/{id}/role");
        var role = Assert.Single(response.Data.GetProperty("roles").EnumerateArray());
        Assert.Equal("doctor", role.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Non_admin_role_change_is_403()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var (_, _, _, targetId) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PatchJsonAsync(
            Client, $"/v1/user/{targetId}/role", new { role = "admin" }, bearer: login.AccessToken);

        AssertErrorEnvelope(response, 403, "COMMON.FORBIDDEN");
    }

    [Fact]
    public async Task Unknown_target_is_404_and_unknown_role_fails_validation()
    {
        var (bearer, _) = await AdminBearerAsync();
        var missing = Guid.NewGuid();

        var notFound = await Api.PatchJsonAsync(
            Client, $"/v1/user/{missing}/role", new { role = "doctor" }, bearer: bearer);
        var issue = AssertErrorEnvelope(notFound, 404, "COMMON.NOT_FOUND");
        Assert.Equal($"User with id {missing} not found", issue.GetProperty("message").GetString());

        var (_, _, _, id) = await SeedPatientAsync();
        var badRole = await Api.PatchJsonAsync(
            Client, $"/v1/user/{id}/role", new { role = "warlock" }, bearer: bearer);
        AssertValidationFailure(badRole, "role", "isIn");
    }
}
