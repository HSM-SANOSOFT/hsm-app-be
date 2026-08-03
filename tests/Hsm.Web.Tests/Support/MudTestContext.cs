using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Hsm.Web.Tests;

/// <summary>
/// Base context for rendering the MudBlazor-based shell components: MudBlazor
/// services registered, JS interop in loose mode (Mud components probe the
/// browser), and an error-boundary logger so ErrorBoundary can render.
/// These tests are DB-free and carry no Infra trait — they run in the CI
/// unit job.
/// </summary>
public abstract class MudTestContext : BunitContext, IAsyncLifetime
{
    protected MudTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IErrorBoundaryLogger, NoopErrorBoundaryLogger>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // MudBlazor registers IAsyncDisposable-only services; xunit v2 tears the
    // context down through this instead of the synchronous Dispose.
    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    private sealed class NoopErrorBoundaryLogger : IErrorBoundaryLogger
    {
        public ValueTask LogErrorAsync(Exception exception) => ValueTask.CompletedTask;
    }
}
