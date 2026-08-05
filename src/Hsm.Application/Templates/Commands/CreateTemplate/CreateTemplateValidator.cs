using FluentValidation;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Commands.CreateTemplate;

/// <summary>
/// Create requires a name and a known category; every other
/// category-conditional shape rule (email/doc/sms block requirements, schema
/// well-formedness, Handlebars compile check) is a full reshape left to
/// Task 8, not ported here.
/// </summary>
public sealed class CreateTemplateValidator : AbstractValidator<CreateTemplateCommand>
{
    public CreateTemplateValidator()
    {
        RuleFor(x => x.Payload.Name).NotEmpty();
        RuleFor(x => x.Payload.Category)
            .NotEmpty()
            .Must(TemplateCatalog.IsKnownCategory).WithMessage("category is not a known template category.");
    }
}
