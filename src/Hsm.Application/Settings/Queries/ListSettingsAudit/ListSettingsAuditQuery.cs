using Hsm.Application.Abstractions;
using Hsm.Contracts;
using Hsm.Domain.Identity;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings.Queries.ListSettingsAudit;

/// <summary>
/// Audit-trail read for the settings screen (plan U18): the rows
/// <see cref="Commands.UpdateSettings.UpdateSettingsCommand"/> writes, newest
/// first. The frozen REST surface had no such operation, and for a while this
/// query only backed the in-process UI surface — <c>GET /api/v1/settings/audit</c>
/// (the standard-API-surface un-freeze, R1) gave it a real route; secrets are
/// already masked at write time, so the read is a plain projection.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record ListSettingsAuditQuery(string Category, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<AppSettingAudit>>;
