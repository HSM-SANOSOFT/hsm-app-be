using Hsm.Domain.Coms;

namespace Hsm.Application.Coms;

/// <summary>Persistence port for the suppression list.</summary>
public interface IEmailSuppressionStore
{
    /// <summary>The subset of <paramref name="emails"/> present on the suppression list.</summary>
    Task<IReadOnlyList<string>> SuppressedAmongAsync(
        IReadOnlyCollection<string> emails, CancellationToken ct = default);

    /// <summary>Insert-or-ignore against the unique email constraint (first suppression wins).</summary>
    Task AddIfMissingAsync(EmailSuppression suppression, CancellationToken ct = default);
}
