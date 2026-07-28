using System.Text.Json.Nodes;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates;

/// <summary>Persistence port for templates and their parse log.</summary>
public interface ITemplateStore
{
    /// <summary>Tracked lookup by primary key.</summary>
    Task<Template?> FindByIdAsync(
        Guid id, bool withChildren = false, bool withBase = false, CancellationToken ct = default);

    /// <summary>
    /// The frozen id-or-name lookup: a UUID-shaped identifier matches by id OR
    /// name; anything else matches by name only.
    /// </summary>
    Task<Template?> FindByIdentifierAsync(
        string identifier, bool withChildren = false, bool withBase = false, CancellationToken ct = default);

    /// <summary>All templates (optionally category-filtered), name ASC, children loaded.</summary>
    Task<IReadOnlyList<Template>> ListAsync(string? category, CancellationToken ct = default);

    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>How many templates reference <paramref name="id"/> as their base.</summary>
    Task<int> CountReferencingBaseAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Template entity, CancellationToken ct = default);

    /// <summary>Stages removal of the parent row and its loaded child row.</summary>
    Task RemoveAsync(Template entity, CancellationToken ct = default);

    Task AddParseLogAsync(TemplateParseLog log, CancellationToken ct = default);
}

/// <summary>
/// Raised by <see cref="ITemplateRenderer"/> when Handlebars compilation or
/// rendering fails; the message is the engine's own diagnostic (surfaced in
/// the frozen "Invalid Handlebars template: ..." error).
/// </summary>
public sealed class TemplateRenderException(string message) : Exception(message);

/// <summary>
/// Handlebars rendering port (frozen composeTemplate): HTML escaping on, no
/// custom helpers, and base-template inheritance by injecting the rendered
/// child as <c>body</c> into the base.
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>Throws <see cref="TemplateRenderException"/> when the source does not compile.</summary>
    void AssertCompiles(string content);

    /// <summary>Renders <paramref name="content"/> (wrapped by <paramref name="baseContent"/> when given).</summary>
    string Render(string content, string? baseContent, JsonObject data);
}
