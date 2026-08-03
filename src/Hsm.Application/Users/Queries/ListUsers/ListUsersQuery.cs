using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Queries.ListUsers;

/// <summary>Admin-only paginated listing (frozen findAll): newest first, roles attached.</summary>
[RequireRole(Roles.Admin)]
public sealed record ListUsersQuery(int Page, int Limit) : IQuery<ListUsersResult>;

/// <summary>
/// The page and the facts needed to describe it. No TotalPages: the envelope's
/// ApiEnvelope.Pagination derives it, and one derivation is one place to be
/// wrong.
/// </summary>
public sealed record ListUsersResult(IReadOnlyList<User> Users, int Page, int PageSize, int TotalItems);
