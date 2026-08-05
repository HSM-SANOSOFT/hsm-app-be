using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings.Queries.ListSettingsAudit;

/// <summary>
/// The paging contract (see <see cref="PagingRules"/>), plus the same
/// category rule <see cref="Hsm.Application.Settings.Queries.GetSettings.GetSettingsValidator"/>
/// states for this resource family: "unknown categories are a caller mistake,
/// not an empty read" — this query used to answer an unknown/missing category
/// with a silent empty page, which contradicted that policy on its own sibling
/// route.
/// </summary>
public sealed class ListSettingsAuditValidator : AbstractValidator<ListSettingsAuditQuery>
{
    public ListSettingsAuditValidator()
    {
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(SettingCatalog.IsKnown).WithMessage("category is not a known settings category.");
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}
