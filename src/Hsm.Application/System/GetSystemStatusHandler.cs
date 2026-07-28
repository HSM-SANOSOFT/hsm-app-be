using System.Reflection;
using Hsm.Contracts.Ui;

namespace Hsm.Application.System;

/// <summary>
/// Proving handler for the U8 boundary round trip: a component resolves
/// <see cref="ISystemStatusUiService"/>, the host implementation calls this
/// handler in process. Real use-case handlers follow this shape.
/// </summary>
public sealed class GetSystemStatusHandler
{
    private readonly string _version =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public Task<SystemStatusDto> HandleAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new SystemStatusDto("hsm-app", _version));
}
