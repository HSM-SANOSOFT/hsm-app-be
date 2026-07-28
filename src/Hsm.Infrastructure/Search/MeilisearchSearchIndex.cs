using Hsm.Application.Ports;
using Meilisearch;

namespace Hsm.Infrastructure.Search;

public sealed class MeilisearchSearchIndex(MeilisearchClient client, string indexUid) : ISearchIndex
{
    public async Task IndexAsync(SearchableDocument document, CancellationToken cancellationToken = default)
    {
        var index = client.Index(indexUid);
        var payload = new Dictionary<string, string>(document.Fields)
        {
            ["id"] = document.Id,
        };
        var task = await index.AddDocumentsAsync([payload], primaryKey: "id", cancellationToken).ConfigureAwait(false);
        await client.WaitForTaskAsync(task.TaskUid, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var index = client.Index(indexUid);
        var result = await index.SearchAsync<Dictionary<string, string>>(query, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return [.. result.Hits.Select(hit =>
        {
            var fields = new Dictionary<string, string>(hit);
            var id = fields["id"];
            fields.Remove("id");
            return new SearchHit(id, fields);
        })];
    }
}
