using Hsm.Application.Abstractions;
using Hsm.Contracts;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings.Queries.ListSettingsAudit;

public sealed class ListSettingsAuditHandler(IAppSettingStore store)
    : IRequestHandler<ListSettingsAuditQuery, PagedResult<AppSettingAudit>>
{
    public Task<PagedResult<AppSettingAudit>> HandleAsync(ListSettingsAuditQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ListAuditAsync(request.Category, request.Page, request.PageSize, ct);
    }
}
