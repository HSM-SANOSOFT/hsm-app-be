using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Contracts.Ui;

namespace Hsm.Application.System.Queries.GetSystemStatus;

/// <summary>
/// Proving handler for the U8 boundary round trip: a component resolves
/// <see cref="ISystemStatusUiService"/>, the host implementation dispatches
/// <see cref="GetSystemStatusQuery"/> in process. Real use-case handlers
/// follow this shape.
/// </summary>
public sealed class GetSystemStatusHandler : IRequestHandler<GetSystemStatusQuery, SystemStatusDto>
{
    private readonly string _version =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public Task<SystemStatusDto> HandleAsync(GetSystemStatusQuery request, CancellationToken ct)
        => Task.FromResult(new SystemStatusDto("hsm-app", _version));
}
