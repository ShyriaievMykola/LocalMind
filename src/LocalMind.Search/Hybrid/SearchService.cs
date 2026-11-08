using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;

namespace LocalMind.Search.Hybrid;

/// <summary>
/// Пошук по бібліотеці. Поки підтримує лише <see cref="SearchMode.Semantic"/>;
/// Keyword і Hybrid додамо разом із FTS5. Вектори зі сховища завантажуються в індекс під час першого пошуку.
/// </summary>
public sealed class SearchService(IEmbedder embedder, IVectorIndex index, IDocumentRepository repository)
    : ISearchService, IDisposable
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile bool _isLoaded;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Mode != SearchMode.Semantic)
        {
            throw new LocalMindException($"Режим пошуку {query.Mode} ще не підтримується.");
        }

        await EnsureVectorsLoadedAsync(cancellationToken);
        if (index.Count == 0)
        {
            return [];
        }

        var vectors = await embedder.EmbedAsync([query.Text], EmbeddingPurpose.Query, cancellationToken);
        var scored = index.Search(vectors[0].Span, query.TopK);

        var chunks = (await repository.GetChunksAsync([.. scored.Select(s => s.ChunkId)], cancellationToken))
            .ToDictionary(c => c.Id);

        var paths = new Dictionary<Guid, string>();
        foreach (var documentId in chunks.Values.Select(c => c.DocumentId).Distinct())
        {
            if (await repository.GetDocumentAsync(documentId, cancellationToken) is { } document)
            {
                paths[documentId] = document.Path;
            }
        }

        var results = new List<SearchResult>(scored.Count);
        foreach (var (chunkId, score) in scored)
        {
            if (chunks.TryGetValue(chunkId, out var chunk) && paths.TryGetValue(chunk.DocumentId, out var path))
            {
                results.Add(new SearchResult(chunk, path, score));
            }
        }

        return results;
    }

    /// <inheritdoc />
    public void Dispose() => _loadLock.Dispose();

    private async Task EnsureVectorsLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_isLoaded)
            {
                return;
            }

            await foreach (var (chunkId, vector) in repository.GetVectorsAsync(embedder.ModelName, cancellationToken))
            {
                index.Add(chunkId, vector);
            }

            _isLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
