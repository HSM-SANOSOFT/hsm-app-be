using Hsm.Application.Abstractions;
using Hsm.Application.System.Queries.GetSystemStatus;
using Hsm.Contracts.Ui;
using Hsm.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Web.Tests;

/// <summary>
/// The U8 boundary round trip, rendered (plan U17): a component resolves the
/// contracts-declared UI service, the HOST implementation answers by
/// dispatching the in-process application query through the real pipeline
/// (dispatcher + telemetry/authorization/validation/transaction behaviors) —
/// no fakes on the request-handling path itself. The only double is a no-op
/// <see cref="IUnitOfWork"/>: <see cref="Hsm.Application.Abstractions.Behaviors.TransactionBehavior{TRequest,TResult}"/>
/// needs one constructed for every request type it wraps, even though
/// <see cref="GetSystemStatusQuery"/> is a query and never opens a
/// transaction — bunit's container has no database, so a real
/// <c>EfUnitOfWork</c> is not an option here. Complements
/// tests/Hsm.Tests/Architecture/UiServiceRoundTripTests.cs, which proves the
/// same resolution through the booted host's container (real
/// infrastructure included).
/// </summary>
public sealed class UiServiceRoundTripComponentTests : MudTestContext
{
    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct) =>
            work(ct);
    }

    [Fact]
    public void Rendered_component_reaches_the_in_process_handler_through_the_ui_service()
    {
        // Real pipeline (dispatcher + behaviors), real application handler,
        // real host-side service implementation.
        Services.AddHsmPipeline();
        Services.AddScoped<AmbientPrincipal>();
        Services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());
        Services.AddScoped<IUnitOfWork, NoopUnitOfWork>();
        Services.AddScoped<IRequestHandler<GetSystemStatusQuery, SystemStatusDto>, GetSystemStatusHandler>();
        Services.AddScoped<ISystemStatusUiService, SystemStatusUiService>();

        var cut = Render<SystemStatusPanel>();

        // The handler reports the application name; seeing it in the rendered
        // markup proves the component → contract interface → host
        // implementation → handler chain end to end.
        Assert.Contains("hsm-app", cut.Markup, StringComparison.Ordinal);
    }
}
