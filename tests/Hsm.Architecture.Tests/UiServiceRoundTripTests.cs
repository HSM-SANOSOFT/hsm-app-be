using Hsm.Contracts.Ui;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Architecture.Tests;

public sealed class UiServiceRoundTripTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UiServiceRoundTripTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Contracts_interface_resolves_to_host_implementation_and_reaches_a_handler()
    {
        // The same resolution path a component uses: ask the host's container
        // for the contracts-declared interface, get the host-bound
        // implementation, and complete a round trip into the application layer.
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemStatusUiService>();

        var status = await service.GetStatusAsync();

        Assert.Equal("hsm-app", status.Application);
        Assert.False(string.IsNullOrWhiteSpace(status.Version));
    }
}
