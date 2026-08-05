using Hsm.Application.Abstractions;
using Hsm.Application.Users.Commands.ChangeOwnPassword;
using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Application.Users.Commands.UpdateOwnProfile;
using Hsm.Application.Users.Queries.GetUser;
using Hsm.Application.Users.Queries.ListUsers;
using Hsm.Contracts;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Api.Users;

/// <summary>
/// The users resource. Every delegate does transport work only — bind, dispatch,
/// project, choose a status. There is no authentication call and no role check
/// here: the actor is installed by middleware and the policy rides on the
/// request type, so an endpoint cannot fail open by forgetting either.
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var users = app.MapGroup("/api/v1/users").WithTags("Users");

        // ListUsers/CreateUser/GetUser/UpdateUser are admin-only
        // ([RequireRole(Roles.Admin)] on their request types), so 403 is a
        // real response; UpdateOwnProfile/ChangeOwnPassword carry no role
        // restriction — any authenticated caller reaches them — so only 401
        // applies.
        users.MapGet("/", ListUsers)
            .WithSummary("List users, newest first.")
            .Produces<PagedResult<UserResource>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        users.MapPost("/", CreateUser)
            .WithSummary("Create a staff user.")
            .Produces<UserResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem();

        // /me is registered before /{id:guid} for readability only — the guid
        // constraint means "me" could never match the parameterised route.
        users.MapPatch("/me", UpdateOwnProfile)
            .WithSummary("Update the calling user's own profile.")
            .Produces<UserResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        users.MapPost("/me/password", ChangeOwnPassword)
            .WithSummary("Change the calling user's own password.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        users.MapGet("/{id:guid}", GetUser)
            .WithSummary("Read one user.")
            .Produces<UserResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPatch("/{id:guid}", UpdateUser)
            .WithSummary("Update a user's role.")
            .Produces<UserResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> ListUsers(
        IDispatcher dispatcher,
        CancellationToken ct,
        int page = PagingRules.DefaultPage,
        int pageSize = PagingRules.DefaultPageSize)
    {
        var result = await dispatcher.Send(new ListUsersQuery(page, pageSize), ct);
        return Results.Ok(result.Map(UserResource.From));
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        var created = await dispatcher.Send(
            new CreateStaffUserCommand(
                request.Username,
                request.Email,
                request.FirstName,
                request.SecondName,
                request.FirstLastName,
                request.SecondLastName,
                request.PhoneNumber,
                request.Role,
                request.TempPassword),
            ct);
        return Results.Created($"/api/v1/users/{created.User.Id}", UserResource.From(created));
    }

    private static async Task<IResult> GetUser(Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(UserResource.From(await dispatcher.Send(new GetUserQuery(id), ct)));

    private static async Task<IResult> UpdateUser(
        Guid id, UpdateUserRoleRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(UserResource.From(
            await dispatcher.Send(new ChangeUserRoleCommand(id, request.Role), ct)));

    private static async Task<IResult> UpdateOwnProfile(
        UpdateOwnProfileRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(UserResource.From(await dispatcher.Send(
            new UpdateOwnProfileCommand(request.FirstName, request.Email), ct)));

    private static async Task<IResult> ChangeOwnPassword(
        ChangeOwnPasswordRequest request,
        HttpContext context,
        IDispatcher dispatcher,
        HsmSessionSignIn sessions,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var user = await dispatcher.Send(
            new ChangeOwnPasswordCommand(request.CurrentPassword, request.NewPassword), ct);

        // The change rotated the security stamp, so every cookie for this
        // account — the caller's included — is now stale and would be refused
        // on its next request. Every OTHER session losing its cookie is the
        // point; this one losing it is not, and on the shell it presents as
        // "I changed my password and got bounced to the sign-in screen".
        // RefreshAsync reissues THIS session's cookie with the new stamp.
        //
        // Through HsmSessionSignIn rather than SignInManager.RefreshSignInAsync:
        // the latter rebuilds the principal and carries only `amr` across, so it
        // would drop the session claim and the caller's very next request would
        // be refused for having no session — turning "keep my own session" into
        // the exact sign-out it exists to prevent.
        //
        // Only when the caller actually has a cookie session: an integration
        // authenticated by bearer must not be handed one as a side effect of a
        // password change, and AuthenticateAsync's per-request cache makes the
        // check free.
        if ((await context.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Succeeded)
        {
            await sessions.RefreshAsync(user, ct);
        }

        return Results.NoContent();
    }
}
