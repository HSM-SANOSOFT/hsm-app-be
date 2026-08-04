using FluentValidation;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Queries.ListEmailBatches;

/// <summary>
/// The frozen paging contract (see <see cref="PagingRules"/>), applied to the
/// filter it lives on. ComsEndpoints previously capped page/limit at the HTTP
/// edge (Task 3's sweep dropped that edge check along with the rest of
/// BodyValidator/QueryValidator); this restores the bound in the pipeline so
/// it holds regardless of caller. The rest of Coms's validation is Task 6's.
/// </summary>
public sealed class ListEmailBatchesValidator : AbstractValidator<ListEmailBatchesQuery>
{
    public ListEmailBatchesValidator()
    {
        RuleFor(x => x.Filter.Page).ValidPage();
        RuleFor(x => x.Filter.Limit).ValidPageSize();
    }
}
