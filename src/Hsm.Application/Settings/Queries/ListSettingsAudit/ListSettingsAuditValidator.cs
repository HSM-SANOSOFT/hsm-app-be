using FluentValidation;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Settings.Queries.ListSettingsAudit;

/// <summary>
/// The audit read has no Page field yet — Task 4 adds one when this query
/// joins the general paged-query reshape. Until then, only the size half of
/// the frozen paging contract applies.
/// </summary>
public sealed class ListSettingsAuditValidator : AbstractValidator<ListSettingsAuditQuery>
{
    public ListSettingsAuditValidator()
    {
        RuleFor(x => x.Limit).ValidPageSize();
    }
}
