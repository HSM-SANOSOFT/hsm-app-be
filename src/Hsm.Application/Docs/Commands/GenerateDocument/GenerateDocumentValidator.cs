using FluentValidation;

namespace Hsm.Application.Docs.Commands.GenerateDocument;

/// <summary>Generation needs a template to render and a title for the resulting document.</summary>
public sealed class GenerateDocumentValidator : AbstractValidator<GenerateDocumentCommand>
{
    public GenerateDocumentValidator()
    {
        RuleFor(x => x.TemplateIdentifier).NotEmpty();
        RuleFor(x => x.Title).NotEmpty();
    }
}
