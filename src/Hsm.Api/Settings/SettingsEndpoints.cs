using System.Text.Json.Nodes;
using Hsm.Api.Auth;
using Hsm.Api.Http;
using Hsm.Application.Abstractions;
using Hsm.Application.Settings;
using Hsm.Application.Settings.Commands.UpdateSettings;
using Hsm.Application.Settings.Queries.GetSettings;

namespace Hsm.Api.Settings;

/// <summary>
/// The two frozen /v1/settings operations (settings.controller.ts), both
/// admin-only. Role policy rides on the request types (AuthorizationBehavior);
/// GET and PUT return 200 with the fresh category read-back. Category and
/// per-item key rules moved into GetSettingsValidator/UpdateSettingsValidator
/// (Task 3); the full item-shape reshape is Task 9's.
/// </summary>
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/v1/settings");
        settings.MapGet("", (Delegate)GetSettings);
        settings.MapPut("", (Delegate)UpdateSettings);
    }

    private static async Task<IResult> GetSettings(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var category = ctx.Request.Query.TryGetValue("category", out var values) ? values[^1] : null;

        var view = await dispatcher.Send(new GetSettingsQuery(category ?? string.Empty), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, SettingsJson(view));
    }

    private static async Task<IResult> UpdateSettings(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await ctx.Request.ReadValidatedJsonAsync<UpdateSettingsBody>(ctx.RequestAborted)
            ?? new UpdateSettingsBody(string.Empty, []);
        var updates = body.Settings
            .Select(item => new SettingUpdate(item.Key ?? string.Empty, item.Value))
            .ToList();

        var view = await dispatcher.Send(
            new UpdateSettingsCommand(body.Category, updates), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, SettingsJson(view));
    }

    /// <summary>The frozen UpdateSettingsDto surface.</summary>
    private sealed record UpdateSettingsBody(string Category, List<SettingItemBody> Settings);

    /// <summary>The frozen UpdateSettingItemDto surface.</summary>
    private sealed record SettingItemBody(string? Key, string? Value);

    private static JsonObject SettingsJson(SettingsView view)
    {
        var settings = new JsonArray();
        foreach (var item in view.Settings)
        {
            settings.Add(new JsonObject
            {
                ["key"] = item.Key,
                ["category"] = item.Category,
                ["isSecret"] = item.IsSecret,
                ["isSet"] = item.IsSet,
                ["value"] = item.Value,
            });
        }

        return new JsonObject
        {
            ["category"] = view.Category,
            ["settings"] = settings,
        };
    }
}
