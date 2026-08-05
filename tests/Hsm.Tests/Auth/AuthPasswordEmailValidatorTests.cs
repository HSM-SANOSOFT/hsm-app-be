using Hsm.Application.Auth.Commands.CompleteOnboarding;
using Hsm.Application.Auth.Commands.ResetPassword;
using Hsm.Application.Auth.Commands.Register;

namespace Hsm.Tests.Auth;

/// <summary>
/// Regression: deleting the edge BodyValidator also deleted its password
/// length and email shape checks for these three Auth commands, and none of
/// them had a pipeline validator — public registration accepted a 1-character
/// password. These three stopgap validators (Task 3's fix round) restore
/// just that much; Identity tasks (11-14) own the rest.
/// </summary>
public class AuthPasswordEmailValidatorTests
{
    [Fact]
    public void Register_short_password_is_refused()
    {
        var validator = new RegisterValidator();
        var command = new RegisterCommand(
            "user1", "user1@test.local", "short", "First", "Last", null, null, null, null);

        Assert.False(validator.Validate(command).IsValid);
    }

    [Fact]
    public void Register_malformed_email_is_refused()
    {
        var validator = new RegisterValidator();
        var command = new RegisterCommand(
            "user1", "not-an-email", "Long-Enough-Pw1", "First", "Last", null, null, null, null);

        Assert.False(validator.Validate(command).IsValid);
    }

    [Fact]
    public void Register_well_formed_request_is_valid()
    {
        var validator = new RegisterValidator();
        var command = new RegisterCommand(
            "user1", "user1@test.local", "Long-Enough-Pw1", "First", "Last", null, null, null, null);

        Assert.True(validator.Validate(command).IsValid);
    }

    [Fact]
    public void Onboarding_short_password_is_refused()
    {
        var validator = new CompleteOnboardingValidator();
        var command = new CompleteOnboardingCommand("short", "+15555550100", "user1@test.local");

        Assert.False(validator.Validate(command).IsValid);
    }

    [Fact]
    public void Onboarding_well_formed_request_is_valid()
    {
        var validator = new CompleteOnboardingValidator();
        var command = new CompleteOnboardingCommand("Long-Enough-Pw1", "+15555550100", "user1@test.local");

        Assert.True(validator.Validate(command).IsValid);
    }

    [Fact]
    public void Reset_password_short_password_is_refused()
    {
        var validator = new ResetPasswordValidator();
        var command = new ResetPasswordCommand("some-token", "short");

        Assert.False(validator.Validate(command).IsValid);
    }

    [Fact]
    public void Reset_password_well_formed_request_is_valid()
    {
        var validator = new ResetPasswordValidator();
        var command = new ResetPasswordCommand("some-token", "Long-Enough-Pw1");

        Assert.True(validator.Validate(command).IsValid);
    }
}
