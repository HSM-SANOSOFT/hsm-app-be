using FluentValidation;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings.Commands.UpdateSettings;

/// <summary>
/// The category must be one of the four known categories, and every item in the batch
/// must at least name the key it is writing — a blank key cannot be resolved
/// against <see cref="SettingCatalog"/> and would otherwise be silently
/// dropped several layers down.
/// </summary>
public sealed class UpdateSettingsValidator : AbstractValidator<UpdateSettingsCommand>
{
    public UpdateSettingsValidator()
    {
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(SettingCatalog.IsKnown).WithMessage("category is not a known settings category.");
        RuleForEach(x => x.Updates).ChildRules(i => i.RuleFor(u => u.Key).NotEmpty());
    }
}
