using System.Net;

namespace Hsm.Api.Tests.System;

public sealed class HealthSmokeFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_system";
}

public class HealthSmokeTests(HealthSmokeFactory factory) : IClassFixture<HealthSmokeFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Host_boots_and_serves_health()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/v1/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
