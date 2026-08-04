using System.Net.Http.Json;
using System.Text;
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
            "/api/v1/users",
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
            "/api/v1/users",
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

    [Fact]
    public async Task Malformed_json_body_is_a_400_problem_not_a_bare_500()
    {
        // Regression: Task 5's resource endpoints bind their request record
        // straight from the body (minimal API's own inferred-body-parameter
        // binding) instead of going through RequestJsonReader. With
        // RouteHandlerOptions.ThrowOnBadRequest pinned true (Program.cs), a
        // malformed body fails inside that binding step and throws
        // BadHttpRequestException — which HsmExceptionHandler.Map converts to
        // a problem+json response using the exception's own status — rather
        // than a raw JsonException reaching the handler's default 500 arm.
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        using var content = new StringContent("{ not valid json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/api/v1/users", UriKind.Relative), content, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 400);
    }
}
