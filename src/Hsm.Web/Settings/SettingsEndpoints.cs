using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Application.Auth;
using Hsm.Application.Settings;
using Hsm.Domain.Identity;
using Hsm.Domain.Settings;
using Hsm.Web.Api;
using Hsm.Web.Auth;

namespace Hsm.Web.Settings;

/// <summary>
/// The two frozen /v1/settings operations (settings.controller.ts), both
/// admin-only. GET and PUT return 200 with the fresh category read-back.
/// </summary>
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/v1/settings");
        settings.MapGet("", (Delegate)GetSettings);
        settings.MapPut("", (Delegate)UpdateSettings);
    }

    private static async Task<IResult> GetSettings(HttpContext ctx, GetSettingsHandler handler)
    {
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        RequestAuth.RequireRoles(ctx, principal, Roles.Admin);
        await RequestAuth.RequireOnboardingCompletedAsync(ctx, principal);

        var query = QueryValidator.Read(ctx);
        var category = query.RequiredEnum("category", SettingsCategories.All);
        query.RejectUnknownParams();
        query.ThrowIfInvalid();

        var view = await handler.HandleAsync(category);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, SettingsJson(view));
    }

    private static async Task<IResult> UpdateSettings(HttpContext ctx, UpdateSettingsHandler handler)
    {
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        RequestAuth.RequireRoles(ctx, principal, Roles.Admin);
        await RequestAuth.RequireOnboardingCompletedAsync(ctx, principal);

        var body = await BodyValidator.ReadAsync(ctx);
        var category = body.RequiredEnum("category", SettingsCategories.All);
        var items = body.RequiredObjectArray("settings");
        var updates = new List<SettingUpdate>();
        if (items is not null)
        {
            for (var index = 0; index < items.Count; index++)
            {
                var item = ValidateItem(body, items[index], index);
                if (item is not null)
                {
                    updates.Add(item);
                }
            }
        }

        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var view = await handler.HandleAsync(category, updates, principal.Id);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, SettingsJson(view));
    }

    /// <summary>Frozen nested item validation (@ValidateNested over UpdateSettingItemDto).</summary>
    private static SettingUpdate? ValidateItem(BodyValidator body, JsonNode? node, int index)
    {
        if (node is not JsonObject item)
        {
            body.AddFailure(
                $"settings.{index}", "nestedValidation",
                "each value in nested property settings must be either object or array");
            return null;
        }

        string? key = null;
        var keyNode = item["key"];
        if (keyNode is null
            || keyNode.GetValueKind() != JsonValueKind.String
            || keyNode.GetValue<string>().Length == 0)
        {
            body.AddFailure($"settings.{index}.key", "isNotEmpty", $"settings.{index}.key should not be empty");
        }
        else
        {
            key = keyNode.GetValue<string>();
        }

        string? value = null;
        var valueNode = item["value"];
        if (valueNode is not null)
        {
            if (valueNode.GetValueKind() == JsonValueKind.String)
            {
                value = valueNode.GetValue<string>();
            }
            else if (valueNode.GetValueKind() != JsonValueKind.Null)
            {
                body.AddFailure($"settings.{index}.value", "isString", $"settings.{index}.value must be a string");
            }
        }

        foreach (var property in item)
        {
            if (property.Key is not ("key" or "value"))
            {
                body.AddFailure(
                    $"settings.{index}.{property.Key}", "whitelistValidation",
                    $"property {property.Key} should not exist");
            }
        }

        return key is null ? null : new SettingUpdate(key, value);
    }

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
