using LocalMind.Core.Models;
using LocalMind.TestUtilities;

namespace LocalMind.Core.Tests.TestUtilities;

public class FakeEmbedderTests
{
    private static float Dot(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a.Span[i] * b.Span[i];
        }

        return sum;
    }

    [Fact]
    public async Task SameText_GivesSameVector()
    {
        var embedder = new FakeEmbedder();

        var vectors = await embedder.EmbedAsync(["docker bridge мережа", "docker bridge мережа"], EmbeddingPurpose.Passage);

        Assert.Equal(vectors[0].ToArray(), vectors[1].ToArray());
    }

    [Fact]
    public async Task Vectors_HaveConfiguredDimensionsAndUnitLength()
    {
        var embedder = new FakeEmbedder(dimensions: 32);

        var vectors = await embedder.EmbedAsync(["перший текст", "другий текст про ssh", ""], EmbeddingPurpose.Query);

        Assert.Equal(3, vectors.Count);
        Assert.All(vectors, v =>
        {
            Assert.Equal(32, v.Length);
            Assert.Equal(1f, MathF.Sqrt(Dot(v, v)), precision: 5);
        });
    }

    [Fact]
    public async Task TextsWithCommonWords_AreCloserThanUnrelated()
    {
        var embedder = new FakeEmbedder();

        var v = await embedder.EmbedAsync(
            ["docker networking bridge", "docker bridge compose", "рецепт борщу з буряком"],
            EmbeddingPurpose.Passage);

        Assert.True(Dot(v[0], v[1]) > Dot(v[0], v[2]));
    }

    [Fact]
    public async Task EmptyInput_ReturnsEmptyListAndRecordsCall()
    {
        var embedder = new FakeEmbedder();

        var vectors = await embedder.EmbedAsync([], EmbeddingPurpose.Passage);

        Assert.Empty(vectors);
        Assert.Equal([0], embedder.Calls);
    }

    [Fact]
    public async Task Calls_RecordBatchSizes()
    {
        var embedder = new FakeEmbedder();

        await embedder.EmbedAsync(["a", "b", "c"], EmbeddingPurpose.Passage);
        await embedder.EmbedAsync(["d"], EmbeddingPurpose.Query);

        Assert.Equal([3, 1], embedder.Calls);
    }

    [Fact]
    public async Task NullText_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => new FakeEmbedder().EmbedAsync(["ok", null!], EmbeddingPurpose.Passage));
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new FakeEmbedder().EmbedAsync(["text"], EmbeddingPurpose.Passage, cts.Token));
    }

    [Fact]
    public void Constructor_NonPositiveDimensions_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeEmbedder(dimensions: 0));
    }

    [Fact]
    public void Properties_HaveDefaults()
    {
        var embedder = new FakeEmbedder();

        Assert.Equal("fake-bag-of-words", embedder.ModelName);
        Assert.Equal(64, embedder.Dimensions);
        Assert.Equal(512, embedder.MaxTokens);
    }
}
