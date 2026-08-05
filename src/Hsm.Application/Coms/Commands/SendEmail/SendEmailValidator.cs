using FluentValidation;

namespace Hsm.Application.Coms.Commands.SendEmail;

/// <summary>
/// Every recipient must be an email, and a batch needs a template to render.
/// ToEmails/Data are also guarded not-null/not-empty directly: RuleForEach is
/// a no-op on a null collection (nothing to iterate), and SendEmailHandler
/// dereferences both unconditionally — a body-bound command with either field
/// omitted must fail here, not throw a NullReferenceException in the handler.
/// </summary>
public sealed class SendEmailValidator : AbstractValidator<SendEmailCommand>
{
    public SendEmailValidator()
    {
        RuleFor(x => x.ToEmails).NotEmpty();
        RuleForEach(x => x.ToEmails).EmailAddress();
        RuleFor(x => x.EmailTemplate).NotEmpty();
        RuleFor(x => x.Data).NotNull();
    }
}
