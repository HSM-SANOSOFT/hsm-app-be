// See SystemEndpoints.cs for why this is Hsm.Api.SystemStatus rather than
// Hsm.Api.System (the folder name, per this task's file list).
namespace Hsm.Api.SystemStatus;

/// <summary>
/// The <c>GET /api/v1/system/status</c> read model: application version and
/// environment, when the status was assembled, and per-dependency state.
/// <see cref="Components"/> is empty today — see <see cref="SystemEndpoints"/>
/// for why — but the field ships now so a dashboard client does not need a
/// breaking shape change the day a real check is added.
/// </summary>
public sealed record SystemStatusResource(
    string Version,
    string Environment,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ComponentStatusResource> Components);

/// <summary>One dependency's reported (not acted-on) state.</summary>
public sealed record ComponentStatusResource(string Name, string Status, string? Detail);
