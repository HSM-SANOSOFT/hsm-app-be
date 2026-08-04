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
    public async Task System_status_returns_version_environment_and_checkedAt_for_an_anonymous_caller()
    {
        using var client = factory.CreateApiClient();
        var before = DateTimeOffset.UtcNow;

        var response = await client.GetAsync("/api/v1/system/status", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("environment").GetString()));
        Assert.True(body.GetProperty("checkedAt").GetDateTimeOffset() >= before);
        // No components/dependency-state field: no dependency probing exists
        // to back one, and a permanently empty array would misreport "zero
        // problems" rather than "not reported" — see SystemStatusResource.
        Assert.False(body.TryGetProperty("components", out _));
    }

    [Theory]
    [InlineData("/v1/health")]
    [InlineData("/v1/health/version")]
    public async Task The_frozen_v1_health_routes_no_longer_exist(string route)
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync(route, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
