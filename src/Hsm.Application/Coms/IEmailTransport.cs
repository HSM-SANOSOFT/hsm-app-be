namespace Hsm.Application.Coms;

/// <summary>An outbound email as handed to the provider transport.</summary>
public sealed record OutboundEmail(
    string? From, IReadOnlyList<string> To, string Subject, string Html);

/// <summary>
/// Actual SMTP delivery is deployment configuration: this port is implemented
/// by a logging adapter in this repo (a real relay adapter is wired per
/// deployment), returning the provider message id.
/// </summary>
public interface IEmailTransport
{
    Task<string> SendAsync(OutboundEmail email, CancellationToken ct = default);
}
