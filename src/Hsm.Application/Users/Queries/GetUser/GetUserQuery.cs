using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Queries.GetUser;

/// <summary>Admin-only single-user fetch (frozen findUserById).</summary>
[RequireRole(Roles.Admin)]
public sealed record GetUserQuery(Guid UserId) : IQuery<User>;
