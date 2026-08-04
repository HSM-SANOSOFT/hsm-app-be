using FluentValidation;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Queries.ListDocuments;

/// <summary>The frozen paging contract (see <see cref="PagingRules"/>), applied to the filter it lives on.</summary>
public sealed class ListDocumentsValidator : AbstractValidator<ListDocumentsQuery>
{
    public ListDocumentsValidator()
    {
        RuleFor(x => x.Filter.Page).ValidPage();
        RuleFor(x => x.Filter.Limit).ValidPageSize();
    }
}
