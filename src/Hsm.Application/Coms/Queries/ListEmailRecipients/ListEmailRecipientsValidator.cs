using FluentValidation;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Queries.ListEmailRecipients;

/// <summary>
/// The frozen paging contract (see <see cref="PagingRules"/>), applied to the
/// filter it lives on. Same restoration as
/// <see cref="Hsm.Application.Coms.Queries.ListEmailBatches.ListEmailBatchesValidator"/> —
/// see its doc comment. The rest of Coms's validation is Task 6's.
/// </summary>
public sealed class ListEmailRecipientsValidator : AbstractValidator<ListEmailRecipientsQuery>
{
    public ListEmailRecipientsValidator()
    {
        RuleFor(x => x.Filter.Page).ValidPage();
        RuleFor(x => x.Filter.Limit).ValidPageSize();
    }
}
