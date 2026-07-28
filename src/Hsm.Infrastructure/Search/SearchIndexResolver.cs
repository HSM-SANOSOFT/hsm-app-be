using Hsm.Application.Ports;
using Meilisearch;
using Microsoft.Extensions.Configuration;

namespace Hsm.Infrastructure.Search;

/// <summary>
/// Resolves the engine serving each collection from configuration
/// (<c>Search:Collections:{name} = meilisearch</c>). The
/// <c>postgres-fts</c> engine value is reserved: the adapter arrives with
/// the first clinical module (plan follow-up), but the resolution mechanism
/// and configuration shape are fixed here so adding it is additive.
/// </summary>
public sealed class SearchIndexResolver(MeilisearchClient meilisearchClient, IConfiguration configuration) : ISearchIndexResolver
{
    public ISearchIndex Resolve(string collection)
    {
        var engine = configuration[$"Search:Collections:{collection}"]
            ?? throw new SearchCollectionNotConfiguredException(
                $"No search engine configured for collection '{collection}'.");

        return engine switch
        {
            "meilisearch" => new MeilisearchSearchIndex(meilisearchClient, collection),
            "postgres-fts" => throw new SearchCollectionNotConfiguredException(
                $"Collection '{collection}' is configured for postgres-fts, whose adapter arrives with the first clinical module."),
            _ => throw new SearchCollectionNotConfiguredException(
                $"Collection '{collection}' is configured for unknown engine '{engine}'."),
        };
    }
}
