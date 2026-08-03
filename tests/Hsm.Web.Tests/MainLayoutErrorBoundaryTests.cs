using Bunit;
using Hsm.Contracts.Ui;
using Hsm.Web.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Web.Tests;

/// <summary>
/// Error containment (plan U17, DoD C6): a page component throwing an
/// unhandled exception is contained by the layout-level error boundary — the
/// shell around it keeps rendering and stays interactive, which is what
/// "the circuit survives" means at component level.
/// </summary>
public sealed class MainLayoutErrorBoundaryTests : MudTestContext
{
    public MainLayoutErrorBoundaryTests()
    {
        Services.AddSingleton<ICurrentUserUiService>(FakeCurrentUserUiService.WithRoles(UiRoles.Admin));
    }

    private static RenderFragment ThrowingBody => builder =>
    {
        builder.OpenComponent<ThrowingComponent>(0);
        builder.CloseComponent();
    };

    [Fact]
    public void Throwing_page_body_is_contained_and_shows_the_spanish_error_ui()
    {
        var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, ThrowingBody));

        Assert.Contains("Se produjo un error inesperado en esta sección", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Reintentar", cut.Markup, StringComparison.Ordinal);
        // The exception never reached the shell chrome: app bar and nav still render.
        Assert.Contains("HSM — Administración hospitalaria", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Inicio", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_stays_interactive_after_the_contained_failure()
    {
        var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, ThrowingBody));

        // The renderer is still alive: the drawer toggle handles events and
        // re-renders without the contained exception escaping.
        cut.Find("button[aria-label='Alternar menú de navegación']").Click();

        Assert.Contains("Se produjo un error inesperado en esta sección", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("HSM — Administración hospitalaria", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Healthy_page_body_renders_without_the_error_ui()
    {
        var cut = Render<MainLayout>(parameters => parameters
            .Add(layout => layout.Body, (RenderFragment)(builder =>
                builder.AddMarkupContent(0, "<p>contenido de la página</p>"))));

        Assert.Contains("contenido de la página", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Se produjo un error inesperado", cut.Markup, StringComparison.Ordinal);
    }
}
