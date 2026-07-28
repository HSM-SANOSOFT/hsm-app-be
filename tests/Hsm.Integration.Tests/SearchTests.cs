using Hsm.Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

public sealed class SearchTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public Task InitializeAsync()
    {
        _provider = TestServices.Build();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Proving_collection_resolves_and_returns_a_typo_tolerant_match()
    {
        var resolver = _provider.GetRequiredService<ISearchIndexResolver>();
        var index = resolver.Resolve("proving-notes");

        // The shared dev index accumulates one document per run, and Meilisearch
        // returns at most 20 hits by default — so the searched token must be
        // unique to this run's document, not a constant that matches them all.
        var id = Guid.NewGuid().ToString("N");
        await index.IndexAsync(new SearchableDocument(
            id,
            new Dictionary<string, string> { ["title"] = $"hospital admission note {id}" }));

        // Drop the token's last character — one typo; Meilisearch's typo
        // tolerance must still find the document.
        var hits = await index.SearchAsync(id[..^1]);

        Assert.Contains(hits, h => h.Id == id);
    }

    [Fact]
    public void Unconfigured_collection_is_a_typed_failure()
    {
        var resolver = _provider.GetRequiredService<ISearchIndexResolver>();

        Assert.Throws<SearchCollectionNotConfiguredException>(
            () => resolver.Resolve("no-such-collection"));
    }
}
