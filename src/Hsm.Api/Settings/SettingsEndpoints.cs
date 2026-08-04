using Hsm.Application.Abstractions;
using Hsm.Application.Settings;
using Hsm.Application.Settings.Commands.UpdateSettings;
using Hsm.Application.Settings.Queries.GetSettings;
using Hsm.Application.Settings.Queries.ListSettingsAudit;
using Hsm.Contracts;

namespace Hsm.Api.Settings;

/// <summary>
/// The settings resource, plus the audit trail that used to be a UI-service-only
/// call and now gets a real route. Every delegate does transport work only —
/// bind, dispatch, project, choose a status. There is no authentication call
/// and no role check here — the actor is installed by middleware and every
/// request type below is admin-only via <c>[RequireRole(Roles.Admin)]</c> on
/// the request record, enforced once by <c>AuthorizationBehavior</c>.
/// </summary>
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var settings = app.MapGroup("/api/v1/settings").WithTags("Settings");

        settings.MapGet("/", GetSettings)
            .WithSummary("Read a settings category (the frozen four), values masked for secrets.")
            .Produces<SettingsResource>();

        settings.MapPut("/", UpdateSettings)
            .WithSummary("Update a settings category and return the fresh read-back.")
            .Produces<SettingsResource>();

        // Registered before no route below could ever shadow it — the group
        // has no other path segment, so ordering is not actually load-bearing
        // here, unlike Templates' {id} vs /validate; kept for readability.
        settings.MapGet("/audit", ListSettingsAudit)
            .WithSummary("Read the settings audit trail for a category, newest first, paged.")
            .Produces<PagedResult<SettingAuditResource>>();
    }

    private static async Task<IResult> GetSettings(
        IDispatcher dispatcher, CancellationToken ct, string? category = null)
    {
        var view = await dispatcher.Send(new GetSettingsQuery(category ?? string.Empty), ct);
        return Results.Ok(SettingsResource.From(view));
    }

    private static async Task<IResult> UpdateSettings(
        UpdateSettingsRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        // Built BEFORE dispatcher.Send: a well-formed body can still carry a
        // null "settings" array or a null entry inside it (System.Text.Json
        // does not enforce this record's non-nullable annotations at
        // deserialization time), and that must degrade to an empty update /
        // an empty key — which UpdateSettingsValidator then turns into a
        // field-keyed 400 — rather than NRE into a bare 500 ahead of the
        // pipeline ever getting a chance to answer.
        var updates = (request.Settings ?? [])
            .Select(item => new SettingUpdate(item?.Key ?? string.Empty, item?.Value))
            .ToList();

        var view = await dispatcher.Send(new UpdateSettingsCommand(request.Category, updates), ct);
        return Results.Ok(SettingsResource.From(view));
    }

    private static async Task<IResult> ListSettingsAudit(
        IDispatcher dispatcher,
        CancellationToken ct,
        string? category = null,
        int page = PagingRules.DefaultPage,
        int pageSize = PagingRules.DefaultPageSize)
    {
        var result = await dispatcher.Send(
            new ListSettingsAuditQuery(category ?? string.Empty, page, pageSize), ct);
        return Results.Ok(result.Map(SettingAuditResource.From));
    }
}
