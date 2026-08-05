using System.Net;
using System.Text.Json;

namespace Hsm.Api.Tests.OpenApi;

public sealed class OpenApiFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_openapi";
}

/// <summary>
/// The committed spec is an ARTIFACT of the surface, and this test is what
/// keeps it one. A route added, renamed, or given a different response type
/// fails here until the author regenerates the file — which makes every wire
/// change a visible line in a diff instead of something a client discovers.
/// </summary>
public class OpenApiSpecTests(OpenApiFactory factory) : IClassFixture<OpenApiFactory>, IAsyncLifetime
{
    /// <summary>Set HSM_OPENAPI_UPDATE=1 to rewrite the committed spec.</summary>
    private const string UpdateVariable = "HSM_OPENAPI_UPDATE";

    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_generated_document_matches_the_committed_one()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/openapi.json", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var generated = Normalize(
            await response.Content.ReadAsStringAsync(CancellationToken.None));

        var path = CommittedSpecPath();
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, generated, CancellationToken.None);
            Assert.Fail($"Rewrote {path}. Re-run without {UpdateVariable} and commit the diff.");
        }

        var committed = Normalize(await File.ReadAllTextAsync(path, CancellationToken.None));
        Assert.True(
            string.Equals(generated, committed, StringComparison.Ordinal),
            $"The API surface changed. Regenerate with:\n" +
            $"  {UpdateVariable}=1 dotnet test tests/Hsm.Api.Tests --filter OpenApiSpecTests\n" +
            $"then review and commit {path}.");
    }

    [Fact]
    public async Task Scalar_is_served_at_the_api_root()
    {
        // Scalar.AspNetCore's own MapScalarApiReference always 302s a bare
        // prefix (no trailing slash, no document name) to the trailing-slash
        // form — relative asset URLs in the emitted HTML depend on it. That is
        // orthogonal to what this test is checking (that a browser landing on
        // /api reaches the reference UI), so this is the one place in the
        // suite that follows the redirect rather than using CreateApiClient's
        // no-redirect default, which exists for the cookie flows under test
        // elsewhere.
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Re-serializes indented so the committed file is diff-readable and
    /// a formatting difference is never mistaken for a surface change.</summary>
    private static string Normalize(string json) =>
        JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement, IndentedOptions);

    private static string CommittedSpecPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hsm.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("Hsm.sln not found."),
            "docs", "reference", "openapi.json");
    }
}
