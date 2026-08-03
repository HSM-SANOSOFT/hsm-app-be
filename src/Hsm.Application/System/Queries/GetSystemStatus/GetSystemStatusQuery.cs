using Hsm.Application.Abstractions;
using Hsm.Contracts.Ui;

namespace Hsm.Application.System.Queries.GetSystemStatus;

/// <summary>
/// Proving query for the U8 boundary round trip: a component resolves
/// <see cref="ISystemStatusUiService"/>, the host implementation dispatches
/// this query in process. Anonymous: the version/name pair carries no PHI and
/// no principal exists before sign-in, yet the shell still renders it.
/// </summary>
[AllowAnonymousRequest]
public sealed record GetSystemStatusQuery : IQuery<SystemStatusDto>;
