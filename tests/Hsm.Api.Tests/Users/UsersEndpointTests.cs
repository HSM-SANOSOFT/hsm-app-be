using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Users;

public sealed class UsersFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_users";
}

public class UsersEndpointTests(UsersFactory factory) : IClassFixture<UsersFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object NewUserBody(string username, string? email = null) => new
    {
        username,
        email = email ?? $"{username}@api.test",
        firstName = "Ada",
        firstLastName = "Lovelace",
        role = Roles.Nurse,
        tempPassword = "Temp-Passw0rd",
    };

    [Fact]
    public async Task Create_returns_201_with_a_location_header_and_the_resource()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var username = $"u{Guid.NewGuid():N}"[..20];

        var response = await client.PostAsJsonAsync(
            "/api/v1/users", NewUserBody(username), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/users/{id}", response.Headers.Location?.ToString());
        Assert.Equal(username, created.GetProperty("username").GetString());
        Assert.Contains(Roles.Nurse, created.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        // The password hash and the soft-delete marker never reach the wire.
        Assert.False(created.TryGetProperty("passwordHash", out _));
        Assert.False(created.TryGetProperty("deletedAt", out _));
    }

    [Fact]
    public async Task A_duplicate_username_is_a_409_not_a_database_error()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var username = $"u{Guid.NewGuid():N}"[..20];
        var first = await client.PostAsJsonAsync(
            "/api/v1/users", NewUserBody(username), CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Identity refuses the second CreateAsync with DuplicateUserName, and
        // IdentityResultExtensions is what turns that into a conflict instead
        // of letting a unique-index violation surface as a bare 500.
        var response = await client.PostAsJsonAsync(
            "/api/v1/users", NewUserBody(username), CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 409);
    }

    [Fact]
    public async Task A_duplicate_email_is_a_409_too()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var shared = $"{Guid.NewGuid():N}@api.test";
        var first = await client.PostAsJsonAsync(
            "/api/v1/users",
            NewUserBody($"u{Guid.NewGuid():N}"[..20], shared),
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            NewUserBody($"u{Guid.NewGuid():N}"[..20], shared),
            CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 409);
    }

    [Fact]
    public async Task List_is_paged_and_reports_its_totals()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync(
            "/api/v1/users?page=1&pageSize=2", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        Assert.True(page.GetProperty("totalItems").GetInt32() >= 1);
        Assert.True(page.GetProperty("totalPages").GetInt32() >= 1);
        Assert.True(page.GetProperty("items").GetArrayLength() <= 2);
    }

    [Fact]
    public async Task Page_size_above_the_cap_is_refused_not_clamped()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/users?pageSize=500", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("pageSize", out _));
    }

    [Fact]
    public async Task Patching_a_users_role_returns_the_updated_resource()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var username = $"u{Guid.NewGuid():N}"[..20];
        var created = await client.PostAsJsonAsync(
            "/api/v1/users", NewUserBody(username), CancellationToken.None);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None))
            .GetProperty("id").GetGuid();

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/users/{id}", new { role = Roles.Doctor }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Contains(Roles.Doctor, updated.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Getting_a_user_by_id_returns_the_resource()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var username = $"u{Guid.NewGuid():N}"[..20];
        var created = await client.PostAsJsonAsync(
            "/api/v1/users", NewUserBody(username), CancellationToken.None);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None))
            .GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/v1/users/{id}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(id, fetched.GetProperty("id").GetGuid());
        Assert.Equal(username, fetched.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Patching_your_own_profile_returns_the_updated_resource()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.PatchAsJsonAsync(
            "/api/v1/users/me",
            new { firstName = "Grace", email = "grace.hopper@api.test" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal("Grace", updated.GetProperty("firstName").GetString());
        Assert.Equal("grace.hopper@api.test", updated.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Changing_your_own_password_returns_204()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = UsersFactory.SeedPassword, newPassword = "New-Passw0rd" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_current_password_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = "not-the-password", newPassword = "New-Passw0rd" },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("currentPassword", out _));
    }

    [Theory]
    [InlineData("GET", "/api/v1/users")]
    [InlineData("POST", "/api/v1/users")]
    public async Task Anonymous_callers_get_401(string method, string route)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task A_non_admin_gets_403_on_the_admin_collection()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.GetAsync("/api/v1/users", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task A_non_guid_id_is_an_unmatched_route()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/users/not-a-guid", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Distinguishes this from An_unknown_user_id_is_a_404_problem below: an
        // unmatched route never reaches HsmExceptionHandler, so there is no
        // problem+json body at all — unlike a handler-produced 404.
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_unknown_user_id_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync($"/api/v1/users/{Guid.NewGuid()}", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }
}
