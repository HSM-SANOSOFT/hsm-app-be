using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Application.Settings.Queries.GetSettings;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings.Commands.UpdateSettings;

public sealed class UpdateSettingsHandler(
    IAppSettingStore store,
    ISettingSeedSource seeds,
    IAuthUnitOfWork unitOfWork,
    ICurrentPrincipal principal,
    IRequestHandler<GetSettingsQuery, SettingsView> reader)
    : IRequestHandler<UpdateSettingsCommand, SettingsView>
{
    public async Task<SettingsView> HandleAsync(UpdateSettingsCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // AuthorizationBehavior has already refused an actor-less dispatch;
        // the throw keeps the same 401 if this ever runs outside the pipeline.
        var actor = principal.Actor ?? throw new UnauthorizedException();

        var rows = await store.FindByKeysAsync(
            [.. request.Updates.Select(u => u.Key).Distinct(StringComparer.Ordinal)], ct);
        var rowByKey = rows.ToDictionary(r => r.Key, StringComparer.Ordinal);

        foreach (var update in request.Updates)
        {
            var def = SettingCatalog.ForKey(update.Key);
            if (def is null || def.Category != request.Category)
            {
                // Never create arbitrary rows.
                continue;
            }

            var incoming = update.Value ?? string.Empty;
            if (def.IsSecret && string.IsNullOrWhiteSpace(incoming))
            {
                // A blank secret leaves the stored value unchanged.
                continue;
            }

            var previous = rowByKey.TryGetValue(def.Key, out var row)
                ? row.Value
                : seeds.SeedValueFor(def.Key);
            if (string.Equals(previous, incoming, StringComparison.Ordinal))
            {
                // Effective value unchanged: no write, no audit.
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            if (row is not null)
            {
                row.Value = incoming;
                row.Category = def.Category;
                row.IsSecret = def.IsSecret;
                row.UpdatedBy = actor.Id;
                row.UpdatedAt = now;
            }
            else
            {
                await store.AddAsync(
                    new AppSetting
                    {
                        Id = Guid.NewGuid(),
                        Key = def.Key,
                        Category = def.Category,
                        IsSecret = def.IsSecret,
                        Value = incoming,
                        UpdatedBy = actor.Id,
                        UpdatedAt = now,
                    },
                    ct);
            }

            await store.AddAuditAsync(
                new AppSettingAudit
                {
                    Id = Guid.NewGuid(),
                    Key = def.Key,
                    Category = def.Category,
                    ChangedBy = actor.Id,
                    OldValue = def.IsSecret
                        ? (string.IsNullOrEmpty(previous) ? null : SettingsPolicy.SecretMask)
                        : previous,
                    NewValue = def.IsSecret ? SettingsPolicy.SecretMask : incoming,
                    ChangedAt = now,
                },
                ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Frozen behavior: respond with the fresh category read-back.
        return await reader.HandleAsync(new GetSettingsQuery(request.Category), ct);
    }
}
