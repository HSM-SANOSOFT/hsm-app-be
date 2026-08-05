using Hsm.Application.Abstractions;
using Hsm.Contracts;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Queries.ListUsers;

/// <summary>Admin-only paginated listing: newest first, roles attached.</summary>
[RequireRole(Roles.Admin)]
public sealed record ListUsersQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResult<UserWithRoles>>;
