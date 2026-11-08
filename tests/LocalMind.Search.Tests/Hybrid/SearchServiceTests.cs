using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using LocalMind.Search.Hybrid;
using LocalMind.Search.Vectors;
using LocalMind.TestUtilities;

namespace LocalMind.Search.Tests.Hybrid;

public sealed class SearchServiceTests : IDisposable
{
    private readonly FakeEmbedder _embedder = new();
    private readonly InMemoryDocumentRepository _repository = new();
    private readonly BruteForceIndex _index;
    private readonly SearchService _service;

    public SearchServiceTests()
    {
        _index = new BruteForceIndex(_embedder.Dimensions);
        _service = new SearchService(_embedder, _index, _repository);
    }

    public void Dispose()
    {
        _service.Dispose();
        _index.Dispose();
    }

    [Fact]
    public async Task Search_ReturnsMostRelevantChunkFirstWithDocumentPath()
    {
        await IndexAsync("/notes/coffee.md", "кава зранку", "чай увечері");
        await IndexAsync("/notes/cars.md", "ремонт машини");

        var results = await _service.SearchAsync(new SearchQuery("кава", SearchMode.Semantic));

        Assert.Equal("кава зранку", results[0].Chunk.Text);
        Assert.Equal("/notes/coffee.md", results[0].DocumentPath);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task Search_RespectsTopK()
    {
        await IndexAsync("/notes/a.md", "один", "два", "три");

        var results = await _service.SearchAsync(new SearchQuery("один", SearchMode.Semantic, topK: 2));

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Search_EmptyLibrary_ReturnsNothingWithoutCallingModel()
    {
        var results = await _service.SearchAsync(new SearchQuery("кава", SearchMode.Semantic));

        Assert.Empty(results);
        Assert.Empty(_embedder.Calls);
    }

    [Fact]
    public async Task Search_LoadsVectorsOnlyOnce()
    {
        await IndexAsync("/notes/a.md", "кава");
        await _service.SearchAsync(new SearchQuery("кава", SearchMode.Semantic));
        await IndexAsync("/notes/b.md", "чай");

        await _service.SearchAsync(new SearchQuery("кава", SearchMode.Semantic));

        Assert.Equal(1, _index.Count);
    }

    [Fact]
    public async Task Search_LoadFailedHalfway_RetryGivesNoDuplicates()
    {
        await IndexAsync("/notes/a.md", "кава", "чай");
        using var inner = new BruteForceIndex(_embedder.Dimensions);
        using var service = new SearchService(_embedder, new FailsOnSecondAddOnce(inner), _repository);
        var query = new SearchQuery("кава", SearchMode.Semantic);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchAsync(query));
        Assert.Equal(1, inner.Count);

        var results = await service.SearchAsync(query);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results.Select(r => r.Chunk.Id).Distinct().Count());
        Assert.Equal(2, inner.Count);
    }

    [Fact]
    public async Task Search_VectorOfDeletedChunk_IsSkipped()
    {
        var document = await IndexAsync("/notes/a.md", "кава");
        await _service.SearchAsync(new SearchQuery("кава", SearchMode.Semantic));

        await _repository.DeleteAsync(document.Id);

        Assert.Empty(await _service.SearchAsync(new SearchQuery("кава", SearchMode.Semantic)));
    }

    [Theory]
    [InlineData(SearchMode.Keyword)]
    [InlineData(SearchMode.Hybrid)]
    public async Task Search_UnsupportedMode_ExplainsError(SearchMode mode)
    {
        var exception = await Assert.ThrowsAsync<LocalMindException>(
            () => _service.SearchAsync(new SearchQuery("кава", mode)));

        Assert.Contains(mode.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_Canceled_Throws()
    {
        await IndexAsync("/notes/a.md", "кава");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.SearchAsync(new SearchQuery("кава", SearchMode.Semantic), cancellation.Token));
    }

    private async Task<Document> IndexAsync(string path, params string[] texts)
    {
        var document = TestData.Document(path);
        var chunks = TestData.Chunks(document.Id, texts);
        var vectors = await _embedder.EmbedAsync(texts, EmbeddingPurpose.Passage);
        await _repository.SaveAsync(document, chunks, vectors, _embedder.ModelName);
        return document;
    }

    private sealed class FailsOnSecondAddOnce(IVectorIndex inner) : IVectorIndex
    {
        private int _adds;

        public int Dimensions => inner.Dimensions;

        public int Count => inner.Count;

        public void Add(Guid chunkId, ReadOnlyMemory<float> vector)
        {
            if (++_adds == 2)
            {
                throw new InvalidOperationException("Збій під час завантаження.");
            }

            inner.Add(chunkId, vector);
        }

        public bool Remove(Guid chunkId) => inner.Remove(chunkId);

        public IReadOnlyList<ScoredChunk> Search(ReadOnlySpan<float> query, int k) => inner.Search(query, k);
    }
}
