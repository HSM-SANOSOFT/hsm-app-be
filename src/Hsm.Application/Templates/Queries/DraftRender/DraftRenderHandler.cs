using Hsm.Application.Abstractions;
using Hsm.Application.Templates.Commands.CreateTemplate;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Queries.DraftRender;

public sealed class DraftRenderHandler(ITemplateStore store, ITemplateRenderer renderer)
    : IRequestHandler<DraftRenderQuery, string>
{
    public async Task<string> HandleAsync(DraftRenderQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? baseContent = null;
        if (!string.IsNullOrEmpty(request.BaseTemplateId))
        {
            baseContent = (await CreateTemplateHandler.ResolveBaseAsync(store, request.BaseTemplateId, ct)).Content;
        }

        try
        {
            return renderer.Render(request.Content, baseContent, request.SampleData ?? []);
        }
        catch (TemplateRenderException exception)
        {
            throw TemplateErrors.InvalidHandlebars(exception.Message);
        }
    }
}
