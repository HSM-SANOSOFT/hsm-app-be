namespace Hsm.Contracts.Ui;

/// <summary>
/// UI service interfaces are declared here, in the boundary's only shared
/// surface, and implemented by the host: in-process handler calls on the
/// server today, REST calls from a WebAssembly host later. Components may
/// depend on these interfaces and nothing deeper.
/// </summary>
public interface ISystemStatusUiService
{
    Task<SystemStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed record SystemStatusDto(string Application, string Version);
