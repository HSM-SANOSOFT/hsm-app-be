using FluentValidation;

namespace Hsm.Application.Docs.Commands.UploadDocuments;

/// <summary>An upload with no files attached has nothing to do.</summary>
public sealed class UploadDocumentsValidator : AbstractValidator<UploadDocumentsCommand>
{
    public UploadDocumentsValidator()
    {
        RuleFor(x => x.Files).NotEmpty();
    }
}
