using Hsm.Application.Abstractions;
using Hsm.Application.System.Queries.GetSystemStatus;
using Hsm.Contracts.Ui;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side implementation of the contracts-declared UI service: dispatches
/// the application query in process. A WebAssembly host would implement the
/// same interface with an HTTP call instead.
/// </summary>
public sealed class SystemStatusUiService(IDispatcher dispatcher) : ISystemStatusUiService
{
    public Task<SystemStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
        => dispatcher.Send(new GetSystemStatusQuery(), cancellationToken);
}
