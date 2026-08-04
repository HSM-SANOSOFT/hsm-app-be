using FluentValidation;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Commands.UpdateTemplate;

/// <summary>
/// Update is a partial patch (frozen PartialType): every field is optional,
/// but a field the caller DID supply must be well-formed. Category is
/// immutable in the frozen handler, so a supplied category is checked against
/// the catalog the same as create's, even though the handler itself decides
/// whether a changed value is accepted.
/// </summary>
public sealed class UpdateTemplateValidator : AbstractValidator<UpdateTemplateCommand>
{
    public UpdateTemplateValidator()
    {
        RuleFor(x => x.Payload.Name).NotEmpty().When(x => x.Payload.Name is not null);
        RuleFor(x => x.Payload.Category)
            .Must(TemplateCatalog.IsKnownCategory).WithMessage("category is not a known template category.")
            .When(x => x.Payload.Category is not null);
    }
}
