using Hsm.Application.System;
using Hsm.Contracts.Ui;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side implementation of the contracts-declared UI service: calls the
/// application handler in process. A WebAssembly host would implement the
/// same interface with an HTTP call instead.
/// </summary>
public sealed class SystemStatusUiService(GetSystemStatusHandler handler) : ISystemStatusUiService
{
    public Task<SystemStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
        => handler.HandleAsync(cancellationToken);
}
