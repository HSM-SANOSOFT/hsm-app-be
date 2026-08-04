using FluentValidation;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Queries.ListEmails;

/// <summary>The frozen paging contract (see <see cref="PagingRules"/>), stated once for this query.</summary>
public sealed class ListEmailsValidator : AbstractValidator<ListEmailsQuery>
{
    public ListEmailsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}
