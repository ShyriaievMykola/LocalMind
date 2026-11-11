using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using LocalMind.Search.Snippets;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Hybrid;

/// <summary>
/// Пошук по бібліотеці в режимах <see cref="SearchMode.Keyword"/> (FTS5, BM25), <see cref="SearchMode.Semantic"/>
/// (embeddings) і <see cref="SearchMode.Hybrid"/> (обидва, злиті <see cref="ReciprocalRankFusion"/>).
/// Keyword працює і без моделі. Вектори зі сховища завантажуються в індекс під час першого семантичного пошуку
/// або заздалегідь через <see cref="LoadVectorsAsync"/>.
/// </summary>
public sealed class SearchService(
    IEmbedder embedder,
    IVectorIndex index,
    IKeywordIndex keywordIndex,
    IDocumentRepository repository,
    IOptions<SearchOptions> options)
    : ISearchService, IDisposable
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile bool _isLoaded;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var scored = query.Mode switch
        {
            SearchMode.Keyword => await keywordIndex.SearchAsync(query.Text, query.TopK, cancellationToken),
            SearchMode.Semantic => await SemanticAsync(query.Text, query.TopK, cancellationToken),
            _ => await HybridAsync(query, cancellationToken),
        };

        return await ToResultsAsync(scored, query.Text, cancellationToken);
    }

    private async Task<IReadOnlyList<ScoredChunk>> HybridAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        // Більше кандидатів з кожного індексу — фрагмент, середній в обох списках, має шанс піднятися нагору.
        var candidates = Math.Max(options.Value.CandidatesPerIndex, query.TopK);
        var keyword = await keywordIndex.SearchAsync(query.Text, candidates, cancellationToken);
        var semantic = await SemanticAsync(query.Text, candidates, cancellationToken);

        return ReciprocalRankFusion.Fuse([keyword, semantic], options.Value.RrfK, query.TopK);
    }

    private async Task<IReadOnlyList<ScoredChunk>> SemanticAsync(string text, int k, CancellationToken cancellationToken)
    {
        await LoadVectorsAsync(cancellationToken);
        if (index.Count == 0)
        {
            return [];
        }

        var vectors = await embedder.EmbedAsync([text], EmbeddingPurpose.Query, cancellationToken);
        return index.Search(vectors[0].Span, k);
    }

    private async Task<IReadOnlyList<SearchResult>> ToResultsAsync(
        IReadOnlyList<ScoredChunk> scored,
        string queryText,
        CancellationToken cancellationToken)
    {
        if (scored.Count == 0)
        {
            return [];
        }

        var chunks = (await repository.GetChunksAsync([.. scored.Select(s => s.ChunkId)], cancellationToken))
            .ToDictionary(c => c.Id);

        var paths = (await repository.GetDocumentsAsync([.. chunks.Values.Select(c => c.DocumentId).Distinct()], cancellationToken))
            .ToDictionary(d => d.Id, d => d.Path);

        var results = new List<SearchResult>(scored.Count);
        foreach (var (chunkId, score) in scored)
        {
            // Фрагмент могли видалити між пошуком в індексі й читанням зі сховища.
            if (chunks.TryGetValue(chunkId, out var chunk) && paths.TryGetValue(chunk.DocumentId, out var path))
            {
                results.Add(new SearchResult(chunk, path, score, SnippetBuilder.Build(chunk, queryText)));
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
