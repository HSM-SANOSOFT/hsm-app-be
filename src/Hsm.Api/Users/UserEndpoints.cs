using System.Globalization;
using System.Text.Json.Nodes;
using Hsm.Api.Auth;
using Hsm.Api.Http;
using Hsm.Application;
using Hsm.Application.Abstractions;
using Hsm.Application.Users.Commands.ChangeOwnPassword;
using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Application.Users.Commands.UpdateOwnProfile;
using Hsm.Application.Users.Queries.GetUser;
using Hsm.Application.Users.Queries.ListUsers;
using Hsm.Domain.Identity;

namespace Hsm.Api.Users;

/// <summary>
/// The six frozen /v1/user operations (user.controller.ts). Each endpoint now
/// does transport work only — authenticate, bind the request, dispatch,
/// render; shape and business-rule validation moved into the pipeline
/// (Task 3's FluentValidation validators). Role and onboarding policy rides on
/// the request types (AuthorizationBehavior); none of the frozen routes
/// carried @AllowPending, so none of these commands is [AllowPendingOnboarding]
/// and a pending non-admin actor is refused by the pipeline.
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

        // Frozen UpdateOwnProfileDto: ONLY firstName and email are reachable
        // through this path — the command has no other field to carry
        // self-escalation with. Shape rules (non-empty when present) now live
        // in UpdateOwnProfileValidator.
        var command = await ctx.Request.ReadValidatedJsonAsync<UpdateOwnProfileCommand>(ctx.RequestAborted)
            ?? new UpdateOwnProfileCommand(null, null);

        var user = await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, UserJson(user, includeRoles: true));
    }

    private static async Task<IResult> ChangeOwnPassword(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var command = await ctx.Request.ReadValidatedJsonAsync<ChangeOwnPasswordCommand>(ctx.RequestAborted)
            ?? new ChangeOwnPasswordCommand(string.Empty, string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> ListUsers(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var page = QueryInt(ctx, "page") ?? 1;
        var limit = QueryInt(ctx, "limit") ?? 20;

        var result = await dispatcher.Send(new ListUsersQuery(page, limit), ctx.RequestAborted);
        var data = new JsonArray([.. result.Users.Select(u => (JsonNode?)UserJson(u, includeRoles: true))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data,
            extra: ApiEnvelope.Pagination(result.Page, result.PageSize, result.TotalItems));
    }

    private static async Task<IResult> CreateStaff(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        // Field names line up 1:1 with the frozen JSON body, so the command
        // record is the wire shape; CreateStaffUserValidator carries every
        // shape and business rule that used to live at this edge.
        var command = await ctx.Request.ReadValidatedJsonAsync<CreateStaffUserCommand>(ctx.RequestAborted)
            ?? new CreateStaffUserCommand(
                string.Empty, string.Empty, string.Empty, null, string.Empty, null, null,
                string.Empty, string.Empty);

        var created = await dispatcher.Send(command, ctx.RequestAborted);
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

        var body = await ctx.Request.ReadValidatedJsonAsync<ChangeRoleBody>(ctx.RequestAborted);

        var user = await dispatcher.Send(
            new ChangeUserRoleCommand(Guid.Parse(id), body?.Role ?? string.Empty), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, UserJson(user, includeRoles: true));
    }

    private static int? QueryInt(HttpContext ctx, string name) =>
        ctx.Request.Query.TryGetValue(name, out var values)
            && int.TryParse(values[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>The frozen ChangeUserRoleDto: a single role field.</summary>
    private sealed record ChangeRoleBody(string? Role);

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
