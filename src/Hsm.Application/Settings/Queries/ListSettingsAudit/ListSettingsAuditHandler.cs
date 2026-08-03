using Hsm.Application.Abstractions;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings.Queries.ListSettingsAudit;

public sealed class ListSettingsAuditHandler(IAppSettingStore store)
    : IRequestHandler<ListSettingsAuditQuery, IReadOnlyList<AppSettingAudit>>
{
    public Task<IReadOnlyList<AppSettingAudit>> HandleAsync(ListSettingsAuditQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ListAuditAsync(request.Category, request.Limit, ct);
    }
}
