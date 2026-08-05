using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Settings.Queries.GetSettings;

/// <summary>
/// Category read: catalog-driven — every definition of
/// the category is returned, the effective value being the stored row or the
/// deploy-environment seed; secrets are masked (value null when unset).
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record GetSettingsQuery(string Category) : IQuery<SettingsView>;
