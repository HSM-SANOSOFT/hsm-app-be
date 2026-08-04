using Hsm.Application.Abstractions;
using Hsm.Application.Users.Commands.ChangeOwnPassword;
using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Application.Users.Commands.UpdateOwnProfile;
using Hsm.Application.Users.Queries.GetUser;
using Hsm.Application.Users.Queries.ListUsers;
using Hsm.Contracts;

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

        users.MapGet("/", ListUsers)
            .WithSummary("List users, newest first.")
            .Produces<PagedResult<UserResource>>();

        users.MapPost("/", CreateUser)
            .WithSummary("Create a staff user.")
            .Produces<UserResource>(StatusCodes.Status201Created);

        // /me is registered before /{id:guid} for readability only — the guid
        // constraint means "me" could never match the parameterised route.
        users.MapPatch("/me", UpdateOwnProfile)
            .WithSummary("Update the calling user's own profile.")
            .Produces<UserResource>();

        users.MapPost("/me/password", ChangeOwnPassword)
            .WithSummary("Change the calling user's own password.")
            .Produces(StatusCodes.Status204NoContent);

        users.MapGet("/{id:guid}", GetUser)
            .WithSummary("Read one user.")
            .Produces<UserResource>();

        users.MapPatch("/{id:guid}", UpdateUser)
            .WithSummary("Update a user's role.")
            .Produces<UserResource>();
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
        ChangeOwnPasswordRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        await dispatcher.Send(
            new ChangeOwnPasswordCommand(request.CurrentPassword, request.NewPassword), ct);
        return Results.NoContent();
    }
}
