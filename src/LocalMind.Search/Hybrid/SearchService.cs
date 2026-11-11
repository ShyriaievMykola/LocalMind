using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using LocalMind.Search.Snippets;

namespace LocalMind.Search.Hybrid;

/// <summary>
/// Пошук по бібліотеці. Поки підтримує лише <see cref="SearchMode.Semantic"/>;
/// Keyword і Hybrid додамо разом із FTS5. Вектори зі сховища завантажуються в індекс під час першого пошуку
/// або заздалегідь через <see cref="LoadVectorsAsync"/>.
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

        await LoadVectorsAsync(cancellationToken);
        if (index.Count == 0)
        {
            return [];
        }

        var vectors = await embedder.EmbedAsync([query.Text], EmbeddingPurpose.Query, cancellationToken);
        var scored = index.Search(vectors[0].Span, query.TopK);

        var chunks = (await repository.GetChunksAsync([.. scored.Select(s => s.ChunkId)], cancellationToken))
            .ToDictionary(c => c.Id);

        var paths = (await repository.GetDocumentsAsync([.. chunks.Values.Select(c => c.DocumentId).Distinct()], cancellationToken))
            .ToDictionary(d => d.Id, d => d.Path);

        var results = new List<SearchResult>(scored.Count);
        foreach (var (chunkId, score) in scored)
        {
            if (chunks.TryGetValue(chunkId, out var chunk) && paths.TryGetValue(chunk.DocumentId, out var path))
            {
                results.Add(new SearchResult(chunk, path, score, SnippetBuilder.Build(chunk, query.Text)));
            }
        }

        return results;
    }

    /// <inheritdoc />
    public void Dispose() => _loadLock.Dispose();

    /// <summary>
    /// Завантажує вектори зі сховища в індекс, якщо цього ще не зроблено. Хост викликає під час запуску,
    /// щоб перший пошук не чекав; повторні виклики нічого не роблять.
    /// </summary>
    public async Task LoadVectorsAsync(CancellationToken cancellationToken = default)
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

            // Якщо завантаження обірветься, наступний пошук пройде його заново:
            // Add замінює вже доданий вектор, тому дублікатів не буде.
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
