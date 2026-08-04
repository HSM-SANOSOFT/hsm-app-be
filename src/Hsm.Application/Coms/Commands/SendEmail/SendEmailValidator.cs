using FluentValidation;

namespace Hsm.Application.Coms.Commands.SendEmail;

/// <summary>Every recipient must be an email, and a batch needs a template to render.</summary>
public sealed class SendEmailValidator : AbstractValidator<SendEmailCommand>
{
    public SendEmailValidator()
    {
        RuleForEach(x => x.ToEmails).EmailAddress();
        RuleFor(x => x.EmailTemplate).NotEmpty();
    }
}
