using System.Text.Json;

namespace Hsm.Contract.Tests.Users;

/// <summary>
/// GET /v1/user/{id} — admin-only single-user fetch, pinned from the frozen
/// users.service findUserById.
/// </summary>
public sealed class GetUserContractTests(UsersApiFactory factory)
    : UsersContractTest(factory), IClassFixture<UsersApiFactory>
{
    [Fact]
    public async Task Admin_fetches_a_user_with_roles()
    {
        var (bearer, _) = await AdminBearerAsync();
        var (username, _, email, id) = await SeedPatientAsync();

        var response = await Api.GetAsync(Client, $"/v1/user/{id}", bearer: bearer);

        AssertSuccessEnvelope(response, 200, $"/v1/user/{id}");
        var data = response.Data;
        Assert.Equal(id.ToString(), data.GetProperty("id").GetString());
        Assert.Equal(username, data.GetProperty("username").GetString());
        Assert.Equal(email, data.GetProperty("email").GetString());
        Assert.False(data.TryGetProperty("password", out _));
        var role = Assert.Single(data.GetProperty("roles").EnumerateArray());
        Assert.Equal("patient", role.GetProperty("role").GetString());
        Assert.Equal("Patient", role.GetProperty("domain").GetString());
        Assert.Equal(JsonValueKind.String, data.GetProperty("onboardingCompletedAt").ValueKind);
        Assert.True(data.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Unknown_id_is_404_with_the_frozen_message()
    {
        var (bearer, _) = await AdminBearerAsync();
        var missing = Guid.NewGuid();

        var response = await Api.GetAsync(Client, $"/v1/user/{missing}", bearer: bearer);

        var issue = AssertErrorEnvelope(response, 404, "COMMON.NOT_FOUND");
        Assert.Equal($"User with id {missing} not found", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Non_uuid_id_is_500_as_in_the_frozen_system()
    {
        // The frozen route had no UUID pipe: the raw string reached the
        // database driver and the cast failure escaped as a bare 500. Pinned
        // (with the U12 envelope divergence: our 500 still rides the envelope).
        var (bearer, _) = await AdminBearerAsync();

        var response = await Api.GetAsync(Client, "/v1/user/not-a-uuid", bearer: bearer);

        AssertErrorEnvelope(response, 500, "COMMON.INTERNAL");
    }

    [Fact]
    public async Task Non_admin_is_403()
    {
        var (username, password, _, id) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.GetAsync(Client, $"/v1/user/{id}", bearer: login.AccessToken);

        AssertErrorEnvelope(response, 403, "COMMON.FORBIDDEN");
    }
}
