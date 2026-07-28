namespace Hsm.Application.Ports;

/// <summary>
/// Search port, resolved per collection: administrative/reference collections
/// bind to the Meilisearch adapter; clinical collections will bind to a
/// PostgreSQL FTS adapter that arrives with the first clinical module. Which
/// engine serves which collection is configuration, not code.
/// </summary>
public interface ISearchIndexResolver
{
    /// <exception cref="SearchCollectionNotConfiguredException">
    /// No engine is configured for <paramref name="collection"/>.
    /// </exception>
    ISearchIndex Resolve(string collection);
}

public interface ISearchIndex
{
    Task IndexAsync(SearchableDocument document, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

public sealed record SearchableDocument(string Id, IReadOnlyDictionary<string, string> Fields);

public sealed record SearchHit(string Id, IReadOnlyDictionary<string, string> Fields);

public class SearchCollectionNotConfiguredException : Exception
{
    public SearchCollectionNotConfiguredException()
    {
    }

    public SearchCollectionNotConfiguredException(string message)
        : base(message)
    {
    }

    public SearchCollectionNotConfiguredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
