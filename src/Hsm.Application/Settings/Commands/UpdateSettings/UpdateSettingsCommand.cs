using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Settings.Commands.UpdateSettings;

/// <summary>
/// Settings update (frozen SettingsService.update): every row write AND its
/// audit entry commit in ONE transaction (R11) — settings can never change
/// without a matching audit trail. Unknown or category-mismatched keys are
/// ignored, a blank value never overwrites a secret, and no-op writes produce
/// no audit entry. Audit rows record the actor and the old/new values, with
/// secrets masked on both sides.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record UpdateSettingsCommand(string Category, IReadOnlyList<SettingUpdate> Updates)
    : ICommand<SettingsView>;
