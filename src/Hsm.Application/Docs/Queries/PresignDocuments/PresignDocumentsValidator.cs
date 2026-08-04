using FluentValidation;

namespace Hsm.Application.Docs.Queries.PresignDocuments;

/// <summary>
/// An explicit expiry must fall within S3's own presigned-URL ceiling (7 days,
/// 604,800 seconds); omitted, the signer's default applies and there is
/// nothing to check.
/// </summary>
public sealed class PresignDocumentsValidator : AbstractValidator<PresignDocumentsQuery>
{
    public PresignDocumentsValidator()
    {
        RuleFor(x => x.ExpiresInSeconds)
            .InclusiveBetween(1, 604_800)
            .When(x => x.ExpiresInSeconds.HasValue);
    }
}
