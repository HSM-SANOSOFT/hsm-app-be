using FluentValidation;
using FluentValidation.Results;
using Hsm.Application.Errors;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// One translation from IdentityResult to the closed exception set, so every
/// handler refuses the same way. Duplicate keys are conflicts; everything else
/// Identity reports is a problem with what the caller sent.
/// </summary>
public static class IdentityResultExtensions
{
    public static void ThrowIfFailed(this IdentityResult result, string field)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Succeeded)
        {
            return;
        }

        if (result.Errors.Any(e =>
                e.Code is "DuplicateUserName" or "DuplicateEmail" or "DuplicateRoleName"))
        {
            throw new ConflictException(
                string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        throw new ValidationException(
            result.Errors.Select(e => new ValidationFailure(field, e.Description)));
    }

    /// <summary>Whether Identity reported a specific error code.</summary>
    public static bool HasCode(this IdentityResult result, string code)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Errors.Any(e => string.Equals(e.Code, code, StringComparison.Ordinal));
    }
}
