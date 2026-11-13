using LocalMind.Search.Configuration;
using LocalMind.Search.Vectors;

namespace LocalMind.Search.Tests.Vectors;

/// <summary>
/// Recall HNSW проти точного пошуку на векторах розмірності моделі (384): рівномірний шум (найважчий випадок —
/// ближчі сусіди майже не відрізняються від решти) і кластеризовані дані (схожі на справжні embeddings).
/// Пороги — реалістичні: recall залежить від <c>efSearch</c>, див. DR-023.
/// </summary>
public class HnswIndexRecallTests
{
    private const int Dimensions = 384;
    private const int QueryCount = 100;

    private static readonly Lazy<Corpus> Uniform10k = new(() => Corpus.Build(10_000, clustered: false, seed: 21));
    private static readonly Lazy<Corpus> Clustered10k = new(() => Corpus.Build(10_000, clustered: true, seed: 22));

    [Fact]
    public void Search_EfEqualToCount_FindsEverything_SoTheGraphIsConnected()
    {
        var corpus = Corpus.Build(3_000, clustered: false, seed: 23);

        Assert.True(corpus.Recall(efSearch: 3_000) >= 0.999, "Граф незв'язний: пошук з ef = N не знайшов усіх сусідів.");
    }

    [Fact]
    public void Search_AfterRemovingAndReplacing_GraphStaysConnectedAndAccurate()
    {
        var corpus = Corpus.Build(3_000, clustered: false, seed: 24);
        var removed = corpus.Ids.Where((_, i) => i % 10 < 3).ToList();
        corpus.Hnsw.RemoveRange(removed);
        foreach (var id in removed)
        {
            corpus.Exact.Remove(id);
        }

        var replacements = Corpus.RandomVectors(300, clustered: false, seed: 25);
        for (var i = 0; i < replacements.Length; i++)
        {
            var id = corpus.Ids[(i * 10) + 5];
            corpus.Hnsw.Add(id, replacements[i]);
            corpus.Exact.Add(id, replacements[i]);
        }

        Assert.Equal(corpus.Exact.Count, corpus.Hnsw.Count);
        Assert.True(corpus.Recall(efSearch: corpus.Hnsw.Count) >= 0.999, "Після видалень і замін граф розірвався.");
    }

    [Fact]
    public void Search_UniformNoise10k_RecallGrowsWithEfSearch()
    {
        var corpus = Uniform10k.Value;

        var at100 = corpus.Recall(100);
        var at400 = corpus.Recall(400);
        var at1000 = corpus.Recall(1000);

        // Виміряно: ≈ 0,53 / 0,91 / 0,99. Рівномірний шум 384-вимірний — найгірший випадок для HNSW.
        Assert.True(at100 >= 0.40, $"ef=100: {at100:0.000}");
        Assert.True(at400 >= 0.85, $"ef=400: {at400:0.000}");
        Assert.True(at1000 >= 0.97, $"ef=1000: {at1000:0.000}");
        Assert.True(at100 < at400 && at400 < at1000);
    }

    [Fact]
    public void Search_ClusteredData10k_RecallIsHighAtModestEfSearch()
    {
        var corpus = Clustered10k.Value;

        var at100 = corpus.Recall(100);
        var at200 = corpus.Recall(200);
        var at400 = corpus.Recall(400);

        // Виміряно: ≈ 0,87 / 0,95 / 1,00.
        Assert.True(at100 >= 0.80, $"ef=100: {at100:0.000}");
        Assert.True(at200 >= 0.92, $"ef=200: {at200:0.000}");
        Assert.True(at400 >= 0.98, $"ef=400: {at400:0.000}");
    }

    private sealed class Corpus
    {
        private readonly float[][] _queries;

        private Corpus(Guid[] ids, HnswIndex hnsw, BruteForceIndex exact, float[][] queries)
        {
            Ids = ids;
            Hnsw = hnsw;
            Exact = exact;
            _queries = queries;
        }

        public Guid[] Ids { get; }

        public HnswIndex Hnsw { get; }

        public BruteForceIndex Exact { get; }

        public static Corpus Build(int count, bool clustered, int seed)
        {
            var vectors = RandomVectors(count, clustered, seed);
            var queries = RandomVectors(QueryCount, clustered, seed, structureSeed: seed, sampleSeed: seed + 1000);
            var ids = Enumerable.Range(0, count).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();
            var hnsw = new HnswIndex(Dimensions, new VectorIndexOptions());
            var exact = new BruteForceIndex(Dimensions);
            for (var i = 0; i < count; i++)
            {
                hnsw.Add(ids[i], vectors[i]);
                exact.Add(ids[i], vectors[i]);
            }

            return new Corpus(ids, hnsw, exact, queries);
        }

        /// <summary>Середній Recall@10 запитів при заданому efSearch; еталон — точний пошук по тих самих векторах.</summary>
        public double Recall(int efSearch)
        {
            // Еталон міг застаріти після видалень: рахуємо його заново.
            var truth = _queries.Select(q => Exact.Search(q, 10).Select(r => r.ChunkId).ToHashSet()).ToArray();
            var total = 0d;
            for (var i = 0; i < _queries.Length; i++)
            {
                total += Hnsw.Search(_queries[i], 10, efSearch).Count(r => truth[i].Contains(r.ChunkId)) / 10d;
            }

            return total / _queries.Length;
        }

        /// <summary>
        /// Рівномірні вектори або кластеризовані (спільний компонент, 50 тем, шум); центри тем залежать від
        /// <paramref name="structureSeed"/>, тож запити беруться з того самого розподілу, що й корпус.
        /// </summary>
        public static float[][] RandomVectors(int count, bool clustered, int seed, int? structureSeed = null, int? sampleSeed = null)
        {
            var structure = new Random(structureSeed ?? seed);
            var common = Gaussian(structure);
            var centers = Enumerable.Range(0, 50).Select(_ => Gaussian(structure)).ToArray();
            var random = new Random(sampleSeed ?? seed + 1);
            var result = new float[count][];
            for (var i = 0; i < count; i++)
            {
                var vector = new float[Dimensions];
                if (clustered)
                {
                    var center = centers[random.Next(centers.Length)];
                    var noise = Gaussian(random);
                    for (var d = 0; d < Dimensions; d++)
                    {
                        vector[d] = (0.8f * common[d]) + center[d] + (0.5f * noise[d]);
                    }
                }
                else
                {
                    for (var d = 0; d < Dimensions; d++)
                    {
                        vector[d] = (float)(random.NextDouble() - 0.5);
                    }
                }

                var norm = Math.Sqrt(vector.Sum(x => (double)x * x));
                for (var d = 0; d < Dimensions; d++)
                {
                    vector[d] = (float)(vector[d] / norm);
                }

                result[i] = vector;
            }

            return result;
        }

        private static float[] Gaussian(Random random)
        {
            var values = new float[Dimensions];
            for (var d = 0; d < Dimensions; d++)
            {
                values[d] = (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
            }

            return values;
        }
    }
}
