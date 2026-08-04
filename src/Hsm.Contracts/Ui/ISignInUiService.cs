namespace Hsm.Contracts.Ui;

/// <summary>
/// Username/password sign-in for the shell (plan U18, screen 1). The host
/// implementation validates credentials through the in-process login handler
/// and issues THE SAME session cookie the REST login sets — one session model
/// for the whole application. Declared here so components never see the
/// application layer; a WebAssembly host would implement this with an HTTP
/// call to POST /api/v1/identity/login instead.
/// </summary>
public interface ISignInUiService
{
    /// <summary>
    /// Validates the credentials and, on success, establishes the browser
    /// session (the session cookie on the current HTTP response). Never throws
    /// for bad credentials — the failure rides in the result so the screen can
    /// render it.
    /// </summary>
    Task<SignInResult> SignInAsync(string username, string password, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a sign-in attempt as the screen needs it.</summary>
public sealed record SignInResult(bool Succeeded, string? Error)
{
    public static SignInResult Success { get; } = new(true, null);

    public static SignInResult Failed(string error) => new(false, error);
}
