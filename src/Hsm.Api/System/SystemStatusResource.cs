// See SystemEndpoints.cs for why this is Hsm.Api.SystemStatus rather than
// Hsm.Api.System (the folder name, per this task's file list).
namespace Hsm.Api.SystemStatus;

/// <summary>
/// The <c>GET /api/v1/system/status</c> read model: application version and
/// environment, and when the status was assembled.
///
/// <para>No <c>Components</c>/dependency-state field: the brief's draft
/// included one, justified by "already surveys the stores" — but no
/// dependency probing exists anywhere in the Application layer, so it would
/// ship as a permanently empty array with zero construction sites anywhere
/// in the solution. An empty list is not neutral to a dashboard reading this
/// response — <c>components: []</c> reads as "zero problems to report," an
/// assertion nobody actually verified, where a genuinely absent field
/// honestly says "this API doesn't tell you." Same precedent as Task 9's
/// <c>SettingItemResource</c> omitting <c>Description</c>: shipping a field
/// that can only ever be empty documents a lie about what this endpoint
/// returns, so it is omitted rather than stubbed.</para>
/// </summary>
public sealed record SystemStatusResource(string Version, string Environment, DateTimeOffset CheckedAt);
