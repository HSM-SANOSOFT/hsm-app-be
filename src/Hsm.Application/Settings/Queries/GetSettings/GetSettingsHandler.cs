using Hsm.Application.Abstractions;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings.Queries.GetSettings;

public sealed class GetSettingsHandler(IAppSettingStore store, ISettingSeedSource seeds)
    : IRequestHandler<GetSettingsQuery, SettingsView>
{
    public async Task<SettingsView> HandleAsync(GetSettingsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definitions = SettingCatalog.ForCategory(request.Category);
        var rows = await store.FindByKeysAsync([.. definitions.Select(d => d.Key)], ct);
        var rowByKey = rows.ToDictionary(r => r.Key, StringComparer.Ordinal);

        var items = definitions
            .Select(def =>
            {
                var hasRow = rowByKey.TryGetValue(def.Key, out var row);
                var raw = hasRow ? row!.Value : seeds.SeedValueFor(def.Key);
                var isSet = !string.IsNullOrEmpty(raw);
                return new SettingItem(
                    def.Key,
                    def.Category,
                    def.IsSecret,
                    isSet,
                    def.IsSecret ? (isSet ? SettingsPolicy.SecretMask : null) : raw,
                    hasRow ? row!.UpdatedAt : null);
            })
            .ToList();

        return new SettingsView(request.Category, items);
    }
}
