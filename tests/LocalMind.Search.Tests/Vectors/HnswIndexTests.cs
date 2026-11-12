using LocalMind.Search.Configuration;
using LocalMind.Search.Vectors;

namespace LocalMind.Search.Tests.Vectors;

public class HnswIndexTests
{
    private const int Dimensions = 64;

    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Third = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidDimensions_Throws(int dimensions)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HnswIndex(dimensions, new VectorIndexOptions()));
    }

    [Fact]
    public void Constructor_InvalidOptions_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HnswIndex(4, new VectorIndexOptions { M = 1 }));
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsNothing()
    {
        using var index = new HnswIndex(2, new VectorIndexOptions());

        Assert.Empty(index.Search([1f, 0f], 5));
    }

    [Fact]
    public void Search_ReturnsMostSimilarFirst()
    {
        using var index = CreateSmallIndex();

        var results = index.Search([1f, 0f], 3);

        Assert.Equal([First, Second, Third], results.Select(r => r.ChunkId));
        Assert.Equal(1d, results[0].Score, 0.0001d);
        Assert.Equal(-1d, results[2].Score, 0.0001d);
    }

    [Fact]
    public void Search_ReturnsOnlyTopK()
    {
        using var index = CreateSmallIndex();

        Assert.Equal(2, index.Search([1f, 0f], 2).Count);
        Assert.Equal(3, index.Search([1f, 0f], 10).Count);
    }

    [Fact]
    public void Search_InvalidArguments_Throw()
    {
        using var index = CreateSmallIndex();

        Assert.Throws<ArgumentException>(() => index.Search([1f, 0f, 0f], 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Search([1f, 0f], 0));
    }

    [Fact]
    public void Add_WrongDimensions_Throws()
    {
        using var index = new HnswIndex(2, new VectorIndexOptions());

        Assert.Throws<ArgumentException>(() => index.Add(First, new float[3]));
    }

    [Fact]
    public void Add_ExistingId_ReplacesVector()
    {
        using var index = CreateSmallIndex();

        index.Add(First, Pair(0f, 1f));

        Assert.Equal(3, index.Count);
        Assert.Equal(Second, index.Search([1f, 0f], 1)[0].ChunkId);
        var closestToY = index.Search([0f, 1f], 1)[0];
        Assert.Equal(First, closestToY.ChunkId);
        Assert.Equal(1d, closestToY.Score, 0.0001d);
    }

    [Fact]
    public void Add_CopiesTheVector()
    {
        using var index = new HnswIndex(2, new VectorIndexOptions());
        var vector = Pair(1f, 0f);
        index.Add(First, vector);

        vector[0] = -1f;

        Assert.Equal(1d, index.Search([1f, 0f], 1)[0].Score, 0.0001d);
    }

    [Fact]
    public void Remove_ExistingId_RemovesIt()
    {
        using var index = CreateSmallIndex();

        Assert.True(index.Remove(First));

        Assert.Equal(2, index.Count);
        Assert.DoesNotContain(First, index.Search([1f, 0f], 10).Select(r => r.ChunkId));
    }

    [Fact]
    public void Remove_UnknownId_ReturnsFalse()
    {
        using var index = CreateSmallIndex();

        Assert.False(index.Remove(Guid.NewGuid()));
        Assert.Equal(3, index.Count);
    }

    [Fact]
    public void Remove_Everything_LeavesWorkingEmptyIndex()
    {
        using var index = CreateSmallIndex();
        index.Remove(First);
        index.Remove(Second);
        index.Remove(Third);

        Assert.Equal(0, index.Count);
        Assert.Empty(index.Search([1f, 0f], 3));

        index.Add(First, Pair(1f, 0f));
        Assert.Equal(First, index.Search([1f, 0f], 1)[0].ChunkId);
    }

    [Fact]
    public void Search_EqualScores_OrderedByChunkIdLikeBruteForce()
    {
        using var index = new HnswIndex(2, new VectorIndexOptions());
        index.Add(Third, Pair(1f, 0f));
        index.Add(First, Pair(1f, 0f));
        index.Add(Second, Pair(1f, 0f));

        Assert.Equal([First, Second, Third], index.Search([1f, 0f], 3).Select(r => r.ChunkId));
    }

    [Fact]
    public void Search_RecallAgainstBruteForce_IsAtLeast095()
    {
        var vectors = RandomVectors(3000, seed: 1);
        using var hnsw = new HnswIndex(Dimensions, new VectorIndexOptions());
        using var exact = new BruteForceIndex(Dimensions);
        Fill(hnsw, exact, vectors);

        var recall = MeanRecall(hnsw, exact, RandomVectors(200, seed: 2), k: 10);

        Assert.True(recall >= 0.95, $"Recall@10 = {recall:0.000}");
    }

    [Fact]
    public void Search_RecallAfterRemovingAndReplacing_StaysAtLeast095()
    {
        var vectors = RandomVectors(3000, seed: 3);
        using var hnsw = new HnswIndex(Dimensions, new VectorIndexOptions());
        using var exact = new BruteForceIndex(Dimensions);
        Fill(hnsw, exact, vectors);

        // Видаляємо третину (серед них, імовірно, й точку входу), решті частині міняємо вектор.
        for (var i = 0; i < vectors.Count; i += 3)
        {
            Assert.True(hnsw.Remove(vectors[i].Id));
            Assert.True(exact.Remove(vectors[i].Id));
        }

        var replacements = RandomVectors(300, seed: 4);
        for (var i = 0; i < replacements.Count; i++)
        {
            var id = vectors[(i * 3) + 1].Id;
            hnsw.Add(id, replacements[i].Vector);
            exact.Add(id, replacements[i].Vector);
        }

        Assert.Equal(exact.Count, hnsw.Count);
        var recall = MeanRecall(hnsw, exact, RandomVectors(200, seed: 5), k: 10);
        Assert.True(recall >= 0.95, $"Recall@10 = {recall:0.000}");
    }

    [Fact]
    public void Search_ByStoredVector_FindsItself()
    {
        var vectors = RandomVectors(500, seed: 6);
        using var index = new HnswIndex(Dimensions, new VectorIndexOptions());
        foreach (var (id, vector) in vectors)
        {
            index.Add(id, vector);
        }

        var found = vectors.Count(v => index.Search(v.Vector, 1)[0].ChunkId == v.Id);

        Assert.True(found >= 495, $"Знайдено {found} з 500");
    }

    [Fact]
    public async Task ParallelSearchesAddsAndRemoves_DoNotCorruptTheIndex()
    {
        var vectors = RandomVectors(1500, seed: 7);
        using var index = new HnswIndex(Dimensions, new VectorIndexOptions());
        foreach (var (id, vector) in vectors.Take(500))
        {
            index.Add(id, vector);
        }

        var queries = RandomVectors(50, seed: 8);
        var writer = Task.Run(() =>
        {
            for (var i = 500; i < vectors.Count; i++)
            {
                index.Add(vectors[i].Id, vectors[i].Vector);
                if (i % 5 == 0)
                {
                    index.Remove(vectors[i - 400].Id);
                }
            }
        });
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var round = 0; round < 20; round++)
            {
                foreach (var query in queries)
                {
                    var results = index.Search(query.Vector, 10);
                    Assert.True(results.Count <= 10);
                    Assert.Equal(results.Count, results.Select(r => r.ChunkId).Distinct().Count());
                }
            }
        }));

        await Task.WhenAll(readers.Append(writer));

        Assert.Equal(index.Count, index.Search(queries[0].Vector, 100000).Count);
    }

    private static float[] Pair(float x, float y) => [x, y];

    private static HnswIndex CreateSmallIndex()
    {
        var index = new HnswIndex(2, new VectorIndexOptions());
        index.Add(Third, Pair(-1f, 0f));
        index.Add(First, Pair(1f, 0f));
        index.Add(Second, Pair(0.6f, 0.8f));
        return index;
    }

    private static void Fill(HnswIndex hnsw, BruteForceIndex exact, List<(Guid Id, float[] Vector)> vectors)
    {
        foreach (var (id, vector) in vectors)
        {
            hnsw.Add(id, vector);
            exact.Add(id, vector);
        }
    }

    private static double MeanRecall(HnswIndex hnsw, BruteForceIndex exact, List<(Guid Id, float[] Vector)> queries, int k)
    {
        var total = 0d;
        foreach (var (_, query) in queries)
        {
            var truth = exact.Search(query, k).Select(r => r.ChunkId).ToHashSet();
            var found = hnsw.Search(query, k).Count(r => truth.Contains(r.ChunkId));
            total += (double)found / k;
        }

        return total / queries.Count;
    }

    private static List<(Guid Id, float[] Vector)> RandomVectors(int count, int seed)
    {
        var random = new Random(seed);
        var result = new List<(Guid, float[])>(count);
        for (var i = 0; i < count; i++)
        {
            var vector = new float[Dimensions];
            var norm = 0d;
            for (var d = 0; d < Dimensions; d++)
            {
                vector[d] = (float)(random.NextDouble() - 0.5);
                norm += vector[d] * vector[d];
            }

            var scale = (float)(1d / Math.Sqrt(norm));
            for (var d = 0; d < Dimensions; d++)
            {
                vector[d] *= scale;
            }

            var bytes = new byte[16];
            random.NextBytes(bytes);
            result.Add((new Guid(bytes), vector));
        }

        return result;
    }
}
