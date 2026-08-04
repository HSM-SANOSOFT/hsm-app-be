using System.Net.Http.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Errors;

public sealed class ValidationProblemFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_validation";
}

public class ValidationProblemTests(ValidationProblemFactory factory)
    : IClassFixture<ValidationProblemFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Validation_failures_render_camelCase_field_keys()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(
            "/v1/user/staff",
            new
            {
                username = "",
                email = "not-an-email",
                firstName = "A",
                firstLastName = "B",
                role = Roles.Patient,
                tempPassword = "short",
            },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("username", out _));
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("role", out _));
        Assert.True(errors.TryGetProperty("tempPassword", out _));
        // PascalCase property names must NOT leak onto the wire.
        Assert.False(errors.TryGetProperty("Username", out _));
    }

    [Fact]
    public async Task All_failures_are_reported_in_one_response()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(
            "/v1/user/staff",
            new
            {
                username = "",
                email = "",
                firstName = "",
                firstLastName = "",
                role = "",
                tempPassword = "",
            },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").EnumerateObject().Count() >= 5);
    }
}
