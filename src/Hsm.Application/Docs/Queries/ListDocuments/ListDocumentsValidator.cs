using FluentValidation;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Queries.ListDocuments;

/// <summary>The paging contract (see <see cref="PagingRules"/>), stated once for this query.</summary>
public sealed class ListDocumentsValidator : AbstractValidator<ListDocumentsQuery>
{
    public ListDocumentsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}
