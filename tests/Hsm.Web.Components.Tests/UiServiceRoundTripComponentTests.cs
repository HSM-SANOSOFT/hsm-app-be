using Hsm.Application.System;
using Hsm.Contracts.Ui;
using Hsm.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Web.Components.Tests;

/// <summary>
/// The U8 boundary round trip, rendered (plan U17): a component resolves the
/// contracts-declared UI service, the HOST implementation answers by calling
/// the in-process application handler — no fakes anywhere on the path.
/// Complements tests/Hsm.Architecture.Tests/UiServiceRoundTripTests.cs, which
/// proves the same resolution through the booted host's container.
/// </summary>
public sealed class UiServiceRoundTripComponentTests : MudTestContext
{
    [Fact]
    public void Rendered_component_reaches_the_in_process_handler_through_the_ui_service()
    {
        // Real application handler, real host-side service implementation.
        Services.AddScoped<GetSystemStatusHandler>();
        Services.AddScoped<ISystemStatusUiService, SystemStatusUiService>();

        var cut = Render<SystemStatusPanel>();

        // The handler reports the application name; seeing it in the rendered
        // markup proves the component → contract interface → host
        // implementation → handler chain end to end.
        Assert.Contains("hsm-app", cut.Markup, StringComparison.Ordinal);
    }
}
