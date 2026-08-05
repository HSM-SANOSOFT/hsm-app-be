using FluentValidation;

namespace Hsm.Application.Templates.Queries.DraftRender;

/// <summary>There is nothing to render without source content.</summary>
public sealed class DraftRenderValidator : AbstractValidator<DraftRenderQuery>
{
    public DraftRenderValidator()
    {
        RuleFor(x => x.Content).NotEmpty();
    }
}
