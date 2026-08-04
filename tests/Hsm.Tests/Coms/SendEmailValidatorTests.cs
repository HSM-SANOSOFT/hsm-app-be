using System.Text.Json.Nodes;
using Hsm.Application.Coms.Commands.SendEmail;

namespace Hsm.Tests.Coms;

public class SendEmailValidatorTests
{
    private static readonly SendEmailValidator Validator = new();

    [Fact]
    public void Null_to_emails_is_a_validation_failure_not_a_crash()
    {
        // Regression: a body-bound command with "toEmails" omitted carries a
        // real null (SendEmailHandler/the endpoint both dereference it
        // unconditionally), and RuleForEach alone is silently a no-op on null.
        var command = new SendEmailCommand(null, null, null!, "welcome", new JsonObject(), null);

        var result = Validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendEmailCommand.ToEmails));
    }

    [Fact]
    public void Null_data_is_a_validation_failure_not_a_crash()
    {
        var command = new SendEmailCommand(null, null, ["a@test.local"], "welcome", null!, null);

        var result = Validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendEmailCommand.Data));
    }

    [Fact]
    public void Well_formed_request_is_valid()
    {
        var command = new SendEmailCommand(null, null, ["a@test.local"], "welcome", new JsonObject(), null);

        var result = Validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
