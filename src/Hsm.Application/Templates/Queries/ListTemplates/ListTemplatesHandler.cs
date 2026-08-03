using Hsm.Application.Abstractions;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Queries.ListTemplates;

public sealed class ListTemplatesHandler(ITemplateStore store)
    : IRequestHandler<ListTemplatesQuery, IReadOnlyList<Template>>
{
    public Task<IReadOnlyList<Template>> HandleAsync(ListTemplatesQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return store.ListAsync(request.Category, ct);
    }
}
