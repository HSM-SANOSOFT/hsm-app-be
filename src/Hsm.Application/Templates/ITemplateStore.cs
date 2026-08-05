using Hsm.Domain.Templates;

namespace Hsm.Application.Templates;

/// <summary>Persistence port for templates and their parse log.</summary>
public interface ITemplateStore
{
    /// <summary>Tracked lookup by primary key.</summary>
    Task<Template?> FindByIdAsync(
        Guid id, bool withChildren = false, bool withBase = false, CancellationToken ct = default);

    /// <summary>
    /// The id-or-name lookup: a UUID-shaped identifier matches by id OR
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
