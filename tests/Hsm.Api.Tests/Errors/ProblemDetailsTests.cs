using System.Net.Http.Json;
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

        var response = await client.GetAsync("/api/v1/users", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task Wrong_role_is_a_403_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.GetAsync("/api/v1/users", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task Missing_resource_is_a_404_problem_naming_the_resource()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync(
            $"/api/v1/users/{Guid.NewGuid()}", CancellationToken.None);

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
        var first = await client.PostAsJsonAsync("/api/v1/users", body, CancellationToken.None);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync(CancellationToken.None));

        var second = await client.PostAsJsonAsync("/api/v1/users", body, CancellationToken.None);

        await ProblemAssert.ProblemAsync(second, 409);
    }

    [Fact]
    public async Task Patient_facing_role_is_a_400_problem()
    {
        // Regression: CreateStaffUserHandler's temporary inline guard (kept
        // until Task 3's CreateStaffUserValidator exists) — without it, POST
        // /api/v1/users would silently provision a patient-facing role.
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var username = $"pat{Guid.NewGuid():N}"[..20];
        var body = new
        {
            username,
            email = $"{username}@api.test",
            firstName = "A",
            firstLastName = "B",
            role = Roles.Patient,
            tempPassword = "Temp-Passw0rd",
        };

        var response = await client.PostAsJsonAsync("/api/v1/users", body, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 400);
    }
}
