using Hsm.Application.Abstractions;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Queries.GetTemplate;

public sealed class GetTemplateHandler(ITemplateStore store) : IRequestHandler<GetTemplateQuery, Template>
{
    public async Task<Template> HandleAsync(GetTemplateQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await store.FindByIdentifierAsync(request.Identifier, withChildren: true, withBase: true, ct)
            ?? throw TemplateErrors.NotFound(request.Identifier);
    }
}
