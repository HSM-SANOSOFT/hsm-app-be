using Hsm.Application.Abstractions;

namespace Hsm.Application.Identity.Commands.ForgotPassword;

/// <summary>
/// Begin a password reset. Anonymous by definition — someone who has lost
/// their password has no session.
///
/// The two rules this request is famous for deliberately live in the HANDLER,
/// not in an <see cref="IValidator{TRequest}"/>:
/// <list type="bullet">
/// <item>the per-account limit of five requests per rolling hour is a 429, and
/// a validator failure is a 400;</item>
/// <item>the unknown-account no-op is non-enumeration — a validator that
/// rejected unknown emails would answer the exact question this surface
/// refuses to answer.</item>
/// </list>
/// </summary>
[AllowAnonymousRequest]
public sealed record ForgotPasswordCommand(string Email) : ICommand<Unit>;
