using System.Text.Json;

namespace Hsm.Contract.Tests.Users;

/// <summary>
/// GET /v1/user — admin-only paginated listing, pinned from the frozen
/// users.service findAll: roles attached, createdAt DESC, and the
/// buildPaginationMeta block on metadata.extra.pagination.
/// </summary>
public sealed class ListUsersContractTests(UsersApiFactory factory)
    : UsersContractTest(factory), IClassFixture<UsersApiFactory>
{
    [Fact]
    public async Task Admin_lists_users_with_roles_and_pagination_metadata()
    {
        var (bearer, adminId) = await AdminBearerAsync();
        await SeedPatientAsync();

        var response = await Api.GetAsync(Client, "/v1/user?page=1&limit=100", bearer: bearer);

        AssertSuccessEnvelope(response, 200, "/v1/user?page=1&limit=100");
        var users = response.Data.EnumerateArray().ToList();
        Assert.NotEmpty(users);
        Assert.Contains(users, u => u.GetProperty("id").GetString() == adminId.ToString());
        Assert.All(users, u =>
        {
            Assert.False(u.TryGetProperty("password", out _));
            Assert.False(u.TryGetProperty("passwordHash", out _));
            Assert.Equal(JsonValueKind.Array, u.GetProperty("roles").ValueKind);
        });

        var pagination = response.Metadata.GetProperty("extra").GetProperty("pagination");
        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(100, pagination.GetProperty("pageSize").GetInt32());
        // Sibling test classes seed users concurrently (shared database), so
        // only the internal consistency of the pagination block is asserted —
        // the count-then-page pair races in the frozen findAndCount too.
        var totalItems = pagination.GetProperty("totalItems").GetInt32();
        var totalPages = pagination.GetProperty("totalPages").GetInt32();
        Assert.True(totalItems >= 2, "expected at least the seeded admin and patient");
        Assert.Equal((int)Math.Ceiling(totalItems / 100.0), totalPages);
    }

    [Fact]
    public async Task Listing_pages_newest_first()
    {
        var (bearer, _) = await AdminBearerAsync();
        var (olderUsername, _, _, _) = await SeedPatientAsync();
        var (newerUsername, _, _, _) = await SeedPatientAsync();

        var response = await Api.GetAsync(Client, "/v1/user?limit=100", bearer: bearer);

        AssertSuccessEnvelope(response, 200, "/v1/user?limit=100");
        var usernames = response.Data.EnumerateArray()
            .Select(u => u.GetProperty("username").GetString())
            .ToList();
        var newerIndex = usernames.IndexOf(newerUsername);
        var olderIndex = usernames.IndexOf(olderUsername);
        Assert.True(newerIndex >= 0 && olderIndex >= 0, "seeded users not on the first page");
        Assert.True(newerIndex < olderIndex, "expected createdAt DESC ordering");

        // Default page size caps a page.
        var limited = await Api.GetAsync(Client, "/v1/user?limit=1", bearer: bearer);
        Assert.Single(limited.Data.EnumerateArray());
        Assert.Equal(1, limited.Metadata.GetProperty("extra").GetProperty("pagination").GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Non_admin_is_403()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.GetAsync(Client, "/v1/user", bearer: login.AccessToken);

        AssertErrorEnvelope(response, 403, "COMMON.FORBIDDEN");
    }

    [Fact]
    public async Task Query_bounds_are_validated()
    {
        var (bearer, _) = await AdminBearerAsync();

        AssertValidationFailure(await Api.GetAsync(Client, "/v1/user?page=0", bearer: bearer), "page", "min");
        AssertValidationFailure(await Api.GetAsync(Client, "/v1/user?limit=101", bearer: bearer), "limit", "max");
        AssertValidationFailure(await Api.GetAsync(Client, "/v1/user?page=abc", bearer: bearer), "page", "isInt");
        AssertValidationFailure(await Api.GetAsync(Client, "/v1/user?page=1.5", bearer: bearer), "page", "isInt");
    }

    [Fact]
    public async Task Unknown_query_parameter_is_rejected()
    {
        var (bearer, _) = await AdminBearerAsync();

        var response = await Api.GetAsync(Client, "/v1/user?sort=asc", bearer: bearer);

        AssertValidationFailure(response, "sort", "whitelistValidation");
    }
}
