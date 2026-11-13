using LocalMind.Search.Vectors;

namespace LocalMind.Search.Tests.Vectors;

public class BruteForceIndexTests
{
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Third = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidDimensions_Throws(int dimensions)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BruteForceIndex(dimensions));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NonFiniteVectorOrQuery_IsRejected(float bad)
    {
        using var index = new BruteForceIndex(2);
        float[] valid = [1f, 0f];
        float[] badVector = [bad, 0f];
        float[] badQuery = [0f, bad];
        index.Add(Guid.NewGuid(), valid);

        Assert.Throws<ArgumentException>(() => index.Add(Guid.NewGuid(), badVector));
        Assert.Throws<ArgumentException>(() => index.Search(badQuery, 1));
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsNothing()
    {
        using var index = new BruteForceIndex(2);

        Assert.Empty(index.Search([1f, 0f], 5));
    }

    [Fact]
    public void Search_ReturnsMostSimilarFirst()
    {
        using var index = CreateIndex();

        var results = index.Search([1f, 0f], 3);

        Assert.Equal([First, Second, Third], results.Select(r => r.ChunkId));
        Assert.Equal(1d, results[0].Score, 0.0001d);
        Assert.Equal(-1d, results[2].Score, 0.0001d);
    }

    [Fact]
    public void Search_ReturnsOnlyTopK()
    {
        using var index = CreateIndex();

        var results = index.Search([1f, 0f], 2);

        Assert.Equal([First, Second], results.Select(r => r.ChunkId));
    }

    [Fact]
    public void Search_KGreaterThanCount_ReturnsAll()
    {
        using var index = CreateIndex();

        Assert.Equal(3, index.Search([1f, 0f], 100).Count);
    }

    [Fact]
    public void Search_EqualScores_OrderIsStable()
    {
        using var index = new BruteForceIndex(2);
        index.Add(Third, Vec(1f, 0f));
        index.Add(First, Vec(1f, 0f));
        index.Add(Second, Vec(1f, 0f));

        var results = index.Search([1f, 0f], 2);

        Assert.Equal([First, Second], results.Select(r => r.ChunkId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Search_InvalidK_Throws(int k)
    {
        using var index = CreateIndex();

        Assert.Throws<ArgumentOutOfRangeException>(() => index.Search([1f, 0f], k));
    }

    [Fact]
    public void Search_WrongDimensions_Throws()
    {
        using var index = CreateIndex();

        Assert.Throws<ArgumentException>(() => index.Search([1f, 0f, 0f], 1));
    }

    [Fact]
    public void Add_WrongDimensions_Throws()
    {
        using var index = new BruteForceIndex(2);

        Assert.Throws<ArgumentException>(() => index.Add(First, new float[1]));
    }

    [Fact]
    public void Add_SameId_ReplacesVector()
    {
        using var index = new BruteForceIndex(2);
        index.Add(First, Vec(1f, 0f));

        index.Add(First, Vec(0f, 1f));

        Assert.Equal(1, index.Count);
        Assert.Equal(1d, index.Search([0f, 1f], 1)[0].Score, 0.0001d);
    }

    [Fact]
    public void Add_CopiesVector()
    {
        using var index = new BruteForceIndex(2);
        var vector = Vec(1f, 0f);
        index.Add(First, vector);

        vector[0] = 0f;

        Assert.Equal(1d, index.Search([1f, 0f], 1)[0].Score, 0.0001d);
    }

    [Fact]
    public void Remove_ExistingAndMissing()
    {
        using var index = CreateIndex();

        Assert.True(index.Remove(First));
        Assert.False(index.Remove(First));
        Assert.Equal(2, index.Count);
        Assert.DoesNotContain(index.Search([1f, 0f], 3), r => r.ChunkId == First);
    }

    [Fact]
    public async Task ParallelSearchAndWrites_DoNotCorruptIndex()
    {
        using var index = new BruteForceIndex(2);
        index.Add(First, Vec(1f, 0f));

        var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                var id = Guid.NewGuid();
                index.Add(id, Vec(0f, 1f));
                index.Remove(id);
            }
        }));
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                Assert.Equal(First, index.Search([1f, 0f], 1)[0].ChunkId);
            }
        }));

        await Task.WhenAll(writers.Concat(readers));

        Assert.Equal(1, index.Count);
    }

    private static float[] Vec(float x, float y) => [x, y];

    private static BruteForceIndex CreateIndex()
    {
        var index = new BruteForceIndex(2);
        index.Add(Third, Vec(-1f, 0f));
        index.Add(First, Vec(1f, 0f));
        index.Add(Second, Vec(0f, 1f));
        return index;
    }
}
