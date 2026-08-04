using FluentValidation;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings.Queries.GetSettings;

/// <summary>The category must be one of the frozen four; unknown categories are a caller mistake, not an empty read.</summary>
public sealed class GetSettingsValidator : AbstractValidator<GetSettingsQuery>
{
    public GetSettingsValidator()
    {
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(SettingCatalog.IsKnown).WithMessage("category is not a known settings category.");
    }
}
