using FluentValidation;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Users.Queries.ListUsers;

/// <summary>The paging contract (see <see cref="PagingRules"/>), stated once for this query.</summary>
public sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}
