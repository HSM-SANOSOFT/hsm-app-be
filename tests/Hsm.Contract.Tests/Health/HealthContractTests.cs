using Hsm.Contract.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hsm.Contract.Tests.Health;

/// <summary>
/// The two frozen public health operations (main.controller.ts). The frozen
/// health check ran Terminus with an EMPTY indicator list — always 200 "ok"
/// with empty info/error/details, wrapped in the success envelope — and the
/// version endpoint exposes ONLY the semantic version.
/// </summary>
public sealed class HealthContractTests(HealthContractTests.HealthApiFactory factory)
    : IClassFixture<HealthContractTests.HealthApiFactory>
{
    public sealed class HealthApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Auth:JwtAccessSecret", "contract_test_at_secret_0123456789abcdef");
            builder.UseSetting("Auth:JwtRefreshSecret", "contract_test_rt_secret_0123456789abcdef");
            builder.UseSetting("Auth:CsrfSecret", "contract_test_csrf_secret_0123456789abcdef");
            builder.UseSetting("API_VERSION", "9.9.9-contract");
        }
    }

    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    [Fact]
    public async Task Health_is_public_and_reports_the_frozen_empty_terminus_shape()
    {
        var response = await Api.GetAsync(_client, "/v1/health");

        Assert.Equal(200, response.Status);
        var metadata = response.Metadata;
        Assert.True(metadata.GetProperty("success").GetBoolean());
        Assert.Equal(200, metadata.GetProperty("statusCode").GetInt32());
        Assert.Equal("/v1/health", metadata.GetProperty("path").GetString());
        Assert.Equal("v1", metadata.GetProperty("apiVersion").GetString());

        var report = response.Data;
        Assert.Equal("ok", report.GetProperty("status").GetString());
        // The frozen controller ran health.check([]) — no indicators, so the
        // three report maps are EMPTY objects, not probe results.
        foreach (var section in new[] { "info", "error", "details" })
        {
            var value = report.GetProperty(section);
            Assert.Equal(System.Text.Json.JsonValueKind.Object, value.ValueKind);
            Assert.Empty(value.EnumerateObject());
        }
    }

    [Fact]
    public async Task Version_is_public_and_returns_only_the_semantic_version()
    {
        var response = await Api.GetAsync(_client, "/v1/health/version");

        Assert.Equal(200, response.Status);
        Assert.True(response.Metadata.GetProperty("success").GetBoolean());
        var data = response.Data;
        // API_VERSION wins (frozen main.service.ts resolution order) and the
        // body carries the version alone — no sha/branch/build timestamp.
        Assert.Equal("9.9.9-contract", data.GetProperty("version").GetString());
        Assert.Single(data.EnumerateObject());
    }

    [Fact]
    public async Task Version_falls_back_to_the_build_version_without_API_VERSION()
    {
        using var withoutEnv = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("API_VERSION", null));
        using var client = withoutEnv.CreateClient();

        var response = await Api.GetAsync(client, "/v1/health/version");

        Assert.Equal(200, response.Status);
        var version = response.Data.GetProperty("version").GetString();
        Assert.False(string.IsNullOrEmpty(version));
        // Never build metadata (the frozen endpoint stripped exact-build info).
        Assert.DoesNotContain('+', version);
    }
}
