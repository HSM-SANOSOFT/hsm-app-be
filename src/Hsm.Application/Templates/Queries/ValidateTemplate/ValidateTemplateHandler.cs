using System.Text.Json.Nodes;
using Hsm.Application.Abstractions;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Queries.ValidateTemplate;

public sealed class ValidateTemplateHandler(ITemplateStore store, ITemplateRenderer renderer)
    : IRequestHandler<ValidateTemplateQuery, ValidateTemplateResult>
{
    public async Task<ValidateTemplateResult> HandleAsync(ValidateTemplateQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var template = await store.FindByIdentifierAsync(request.Identifier, withChildren: false, withBase: false, ct)
            ?? throw TemplateErrors.NotFound(request.Identifier);

        var issues = TemplateSchema.Validate(JsonNode.Parse(template.SchemaJson), request.Data);
        if (issues.Count > 0)
        {
            return new ValidateTemplateResult(Valid: false, template.Id, issues);
        }

        try
        {
            renderer.AssertCompiles(template.Content);
        }
        catch (TemplateRenderException exception)
        {
            return new ValidateTemplateResult(
                Valid: false,
                template.Id,
                [new TemplateSchemaIssue("content", "compilable Handlebars", exception.Message)]);
        }

        return new ValidateTemplateResult(Valid: true, template.Id, Issues: null);
    }
}
