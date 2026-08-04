using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hsm.Api.Tests.System;

public class SystemEndpointTests(SystemFactory factory) : IClassFixture<SystemFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Health_returns_200_plain_text_healthy_for_an_anonymous_caller()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task System_status_returns_version_environment_checkedAt_and_components_for_an_anonymous_caller()
    {
        using var client = factory.CreateApiClient();
        var before = DateTimeOffset.UtcNow;

        var response = await client.GetAsync("/api/v1/system/status", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("environment").GetString()));
        Assert.True(body.GetProperty("checkedAt").GetDateTimeOffset() >= before);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("components").ValueKind);
    }

    [Fact]
    public async Task Getting_v1_health_version_no_longer_exists()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/v1/health/version", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
