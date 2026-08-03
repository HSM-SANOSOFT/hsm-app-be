using Hsm.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Tests.Abstractions;

public class DispatcherTests
{
    private sealed record Ping(string Text) : IQuery<string>;

    private sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> HandleAsync(Ping request, CancellationToken ct)
            => Task.FromResult($"pong:{request.Text}");
    }

    [Fact]
    public async Task Dispatches_to_the_handler_registered_for_the_request_type()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<Ping, string>, PingHandler>();
        services.AddSingleton<IDispatcher, Dispatcher>();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDispatcher>()
            .Send(new Ping("hi"), CancellationToken.None);

        Assert.Equal("pong:hi", result);
    }

    [Fact]
    public async Task Missing_handler_throws_a_named_error()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher, Dispatcher>();
        using var provider = services.BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IDispatcher>()
                .Send(new Ping("hi"), CancellationToken.None));

        Assert.Contains(nameof(Ping), ex.Message, StringComparison.Ordinal);
    }
}
