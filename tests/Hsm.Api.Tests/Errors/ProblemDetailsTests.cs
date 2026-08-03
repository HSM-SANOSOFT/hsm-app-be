using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Errors;

public sealed class ProblemDetailsFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_errors";
}

public class ProblemDetailsTests(ProblemDetailsFactory factory)
    : IClassFixture<ProblemDetailsFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unauthenticated_request_is_a_401_problem()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/v1/user", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task Wrong_role_is_a_403_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.GetAsync("/v1/user", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task Missing_resource_is_a_404_problem_naming_the_resource()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync(
            $"/v1/user/{Guid.NewGuid()}", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 404);
        Assert.Contains("User", problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_username_is_a_409_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var username = $"dup{Guid.NewGuid():N}"[..20];
        var body = new
        {
            username,
            email = $"{username}@api.test",
            firstName = "A",
            firstLastName = "B",
            role = Roles.Nurse,
            tempPassword = "Temp-Passw0rd",
        };
        var first = await client.PostAsJsonAsync("/v1/user/staff", body, CancellationToken.None);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync(CancellationToken.None));

        var second = await client.PostAsJsonAsync("/v1/user/staff", body, CancellationToken.None);

        await ProblemAssert.ProblemAsync(second, 409);
    }

    [Fact]
    public async Task Unexpected_failure_is_a_500_problem_with_no_internal_detail()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        // A non-GUID id reaches Guid.Parse and throws FormatException, which is
        // outside the closed set and must surface as a bare 500.
        var response = await client.GetAsync("/v1/user/not-a-guid", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 500);
        Assert.False(problem.TryGetProperty("detail", out var detail) && detail.ValueKind is JsonValueKind.String
            && detail.GetString()!.Contains("FormatException", StringComparison.Ordinal));
    }
}
