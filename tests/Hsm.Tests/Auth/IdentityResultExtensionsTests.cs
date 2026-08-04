using FluentValidation;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Tests.Auth;

/// <summary>
/// <see cref="IdentityResultExtensions"/> is the ONE place an
/// <see cref="IdentityResult"/> becomes an exception, so that every handler
/// refuses the same way and no handler grows a second, differently-shaped
/// mapping. The split it encodes is the same one Task 2's exception set draws
/// everywhere else: a duplicate key is a CONFLICT with existing state (409),
/// and everything else Identity reports is a problem with what the caller sent
/// (400 on a named field).
/// </summary>
public class IdentityResultExtensionsTests
{
    [Theory]
    [InlineData("DuplicateUserName")]
    [InlineData("DuplicateEmail")]
    [InlineData("DuplicateRoleName")]
    public void A_duplicate_key_is_a_conflict(string code)
    {
        var result = IdentityResult.Failed(new IdentityError { Code = code, Description = "Taken." });

        var refusal = Assert.Throws<ConflictException>(() => result.ThrowIfFailed("username"));

        Assert.Contains("Taken.", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Anything_else_is_a_field_validation_failure_on_the_named_field()
    {
        var result = IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = "Too short." });

        var refusal = Assert.Throws<ValidationException>(() => result.ThrowIfFailed("newPassword"));

        var failure = Assert.Single(refusal.Errors);
        Assert.Equal("newPassword", failure.PropertyName);
        Assert.Equal("Too short.", failure.ErrorMessage);
    }

    [Fact]
    public void Every_reported_error_reaches_the_caller_not_just_the_first()
    {
        var result = IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = "Too short." },
            new IdentityError { Code = "PasswordRequiresDigit", Description = "Needs a digit." });

        var refusal = Assert.Throws<ValidationException>(() => result.ThrowIfFailed("password"));

        Assert.Equal(2, refusal.Errors.Count());
    }

    [Fact]
    public void A_mixed_result_containing_a_duplicate_is_a_conflict()
    {
        // Order must not decide the outcome: a duplicate anywhere in the set
        // means the request collided with existing state.
        var result = IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = "Too short." },
            new IdentityError { Code = "DuplicateEmail", Description = "Taken." });

        Assert.Throws<ConflictException>(() => result.ThrowIfFailed("password"));
    }

    [Fact]
    public void A_succeeded_result_throws_nothing()
    {
        IdentityResult.Success.ThrowIfFailed("username");
    }

    [Fact]
    public void HasCode_finds_the_code_that_routes_a_wrong_current_password()
    {
        // ChangeOwnPasswordHandler asks this before delegating to ThrowIfFailed,
        // because PasswordMismatch is about currentPassword while every other
        // failure that call can produce is about newPassword.
        var result = IdentityResult.Failed(
            new IdentityError { Code = "PasswordMismatch", Description = "Incorrect password." });

        Assert.True(result.HasCode("PasswordMismatch"));
        Assert.False(result.HasCode("PasswordTooShort"));
        Assert.False(IdentityResult.Success.HasCode("PasswordMismatch"));
    }
}
