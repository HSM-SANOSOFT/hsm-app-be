using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings.Queries.ListSettingsAudit;

/// <summary>
/// Audit-trail read for the settings screen (plan U18): the rows
/// <see cref="Commands.UpdateSettings.UpdateSettingsCommand"/> writes, newest
/// first. The frozen REST surface has no such operation — this query exists
/// for the in-process UI surface only and adds no /v1 route; secrets are
/// already masked at write time, so the read is a plain projection.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record ListSettingsAuditQuery(string Category, int Limit = 50)
    : IQuery<IReadOnlyList<AppSettingAudit>>;
