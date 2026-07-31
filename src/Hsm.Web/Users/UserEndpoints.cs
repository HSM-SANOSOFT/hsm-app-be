using System.Text.Json.Nodes;
using Hsm.Application;
using Hsm.Application.Abstractions;
using Hsm.Application.Users.Commands.ChangeOwnPassword;
using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Application.Users.Commands.UpdateOwnProfile;
using Hsm.Application.Users.Queries.GetUser;
using Hsm.Application.Users.Queries.ListUsers;
using Hsm.Domain.Identity;
using Hsm.Web.Api;
using Hsm.Web.Auth;

namespace Hsm.Web.Users;

/// <summary>
/// The six frozen /v1/user operations (user.controller.ts). Each endpoint now
/// does transport work only — authenticate, bind and shape-validate the
/// request, dispatch, render. Role and onboarding policy rides on the request
/// types (AuthorizationBehavior); none of the frozen routes carried
/// @AllowPending, so none of these commands is [AllowPendingOnboarding] and a
/// pending non-admin actor is refused by the pipeline.
/// POST returns 201 and GET/PATCH 200, matching the frozen runtime.
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var user = app.MapGroup("/v1/user");

        // Self-service (any authenticated, onboarded user).
        user.MapPatch("/me", (Delegate)UpdateOwnProfile);
        user.MapPost("/me/password", (Delegate)ChangeOwnPassword);

        // Admin user management.
        user.MapGet("", (Delegate)ListUsers);
        user.MapPost("/staff", (Delegate)CreateStaff);
        user.MapGet("/{id}", (Delegate)GetUser);
        user.MapPatch("/{id}/role", (Delegate)ChangeUserRole);
    }

    private static async Task<IResult> UpdateOwnProfile(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        // Frozen UpdateOwnProfileDto: ONLY firstName and email. Any role/roles
        // property is rejected by the whitelist — self-escalation is
        // structurally impossible on this route, and the command has no user id
        // to point somewhere else with.
        var body = await BodyValidator.ReadAsync(ctx);
        var firstName = body.OptionalString("firstName", notEmpty: true);
        var email = body.OptionalString("email", notEmpty: true);
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var user = await dispatcher.Send(
            new UpdateOwnProfileCommand(firstName, email), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, UserJson(user, includeRoles: true));
    }

    private static async Task<IResult> ChangeOwnPassword(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var currentPassword = body.RequiredString("currentPassword");
        var newPassword = body.RequiredString("newPassword", minLength: 8);
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        await dispatcher.Send(
            new ChangeOwnPasswordCommand(currentPassword, newPassword), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> ListUsers(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var query = QueryValidator.Read(ctx);
        var page = query.OptionalInt("page", min: 1) ?? 1;
        var limit = query.OptionalInt("limit", min: 1, max: 100) ?? 20;
        query.RejectUnknownParams();
        query.ThrowIfInvalid();

        var result = await dispatcher.Send(new ListUsersQuery(page, limit), ctx.RequestAborted);
        var data = new JsonArray([.. result.Users.Select(u => (JsonNode?)UserJson(u, includeRoles: true))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data,
            extra: ApiEnvelope.Pagination(result.Page, result.PageSize, result.TotalItems));
    }

    private static async Task<IResult> CreateStaff(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var username = body.RequiredString("username");
        var email = body.RequiredString("email", email: true);
        var firstName = body.RequiredString("firstName");
        var secondName = body.OptionalString("secondName");
        var firstLastName = body.RequiredString("firstLastName");
        var secondLastName = body.OptionalString("secondLastName");
        var phoneNumber = body.OptionalString("phoneNumber");
        // Frozen @IsIn(ROLE_VALUES) — constraint key isIn, not isEnum.
        var role = body.RequiredString("role", oneOf: RoleCatalog.All, oneOfConstraint: "isIn");
        var tempPassword = body.RequiredString("tempPassword", minLength: 8);
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var created = await dispatcher.Send(
            new CreateStaffUserCommand(
                username, email, firstName, secondName, firstLastName,
                secondLastName, phoneNumber, role, tempPassword),
            ctx.RequestAborted);
        // Frozen response: the created row without roles (and of course without
        // any password field).
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, UserJson(created, includeRoles: false));
    }

    private static async Task<IResult> GetUser(HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        // No UUID pipe in the frozen route: a malformed id reached the driver
        // and failed as a 500. Guid.Parse reproduces that observable surface
        // (FormatException → the 500 envelope).
        var user = await dispatcher.Send(new GetUserQuery(Guid.Parse(id)), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, UserJson(user, includeRoles: true));
    }

    private static async Task<IResult> ChangeUserRole(HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var role = body.RequiredString("role", oneOf: RoleCatalog.All, oneOfConstraint: "isIn");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var user = await dispatcher.Send(
            new ChangeUserRoleCommand(Guid.Parse(id), role), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, UserJson(user, includeRoles: true));
    }

    /// <summary>
    /// The frozen user JSON: every column except the password hash, dates as
    /// ISO-8601, role rows attached where the frozen query loaded them.
    /// </summary>
    internal static JsonObject UserJson(User user, bool includeRoles)
    {
        var json = new JsonObject
        {
            ["id"] = user.Id.ToString(),
            ["username"] = user.Username,
            ["email"] = user.Email,
            ["firstName"] = user.FirstName,
            ["secondName"] = user.SecondName,
            ["firstLastName"] = user.FirstLastName,
            ["secondLastName"] = user.SecondLastName,
            ["phoneNumber"] = user.PhoneNumber,
            ["gender"] = user.Gender,
            ["lastLoginAt"] = IsoTimestamp.Of(user.LastLoginAt),
            ["onboardingCompletedAt"] = IsoTimestamp.Of(user.OnboardingCompletedAt),
            ["isActive"] = user.IsActive,
            ["emailVerified"] = user.EmailVerified,
            ["phoneVerified"] = user.PhoneVerified,
            ["createdAt"] = IsoTimestamp.Of(user.CreatedAt),
            ["updatedAt"] = IsoTimestamp.Of(user.UpdatedAt),
            ["deletedAt"] = IsoTimestamp.Of(user.DeletedAt),
        };
        if (includeRoles)
        {
            var roles = new JsonArray();
            foreach (var role in user.Roles)
            {
                roles.Add(new JsonObject
                {
                    ["id"] = role.Id.ToString(),
                    ["domain"] = role.Domain,
                    ["role"] = role.Role,
                    ["createdAt"] = IsoTimestamp.Of(role.CreatedAt),
                });
            }

            json["roles"] = roles;
        }

        return json;
    }
}
