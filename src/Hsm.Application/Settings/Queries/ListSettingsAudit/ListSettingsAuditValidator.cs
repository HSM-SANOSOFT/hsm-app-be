using FluentValidation;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Settings.Queries.ListSettingsAudit;

/// <summary>The frozen paging contract (see <see cref="PagingRules"/>), stated once for this query.</summary>
public sealed class ListSettingsAuditValidator : AbstractValidator<ListSettingsAuditQuery>
{
    public ListSettingsAuditValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}
