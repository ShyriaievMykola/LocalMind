using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using LocalMind.Core.Abstractions;
using LocalMind.Search.Configuration;
using LocalMind.Search.Vectors;

namespace LocalMind.Benchmarks;

/// <summary>
/// Recall@10 проти затримки для HNSW на 1k / 10k / 100k векторів (експеримент E4, R-06).
/// BenchmarkDotNet міряє лише час, а recall — частка точних сусідів повного перебору, яку знайшов HNSW, —
/// тому рахуємо його окремо і пишемо CSV поруч із результатами BenchmarkDotNet. Два набори даних: рівномірні випадкові
/// вектори (<see cref="RandomData"/>, ті самі, що в BenchmarkDotNet; найгірший випадок для графових індексів) і «кластеризовані» —
/// спільний компонент, теми й шум, ближче до справжніх embeddings (така ж модель даних у HnswIndexTests).
/// </summary>
internal static class RecallReport
{
    private const int K = 10;
    private const int QueryCount = 100;
    private const int Passes = 5;

    private static readonly int[] Sizes = [1_000, 10_000, 100_000];
    private static readonly string[] DataKinds = ["uniform", "clustered"];
    private static readonly int[] EfSearchValues = [10, 20, 50, 100, 200, 400];

    // efSearch задається в конструкторі й далі не змінюється; для кривої «recall проти затримки» граф будуємо один раз
    // і міняємо лише ширину пошуку, як це робить параметр у запиті. Граф при цьому той самий, тож порівняння чесне.
    private static readonly FieldInfo EfSearchField = typeof(HnswIndex)
        .GetField("_efSearch", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("У HnswIndex немає поля _efSearch: оновіть RecallReport.");

    public static int Run(string outputPath)
    {
        var lines = new List<string> { "data;size;index;ef_search;recall_at_10;latency_ms_per_query" };
        foreach (var data in DataKinds)
        {
            foreach (var size in Sizes)
            {
                Measure(data, size, lines);
            }
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllLines(outputPath, lines);
        Console.WriteLine($"Збережено: {Path.GetFullPath(outputPath)}");
        return 0;
    }

    private static void Measure(string data, int size, List<string> lines)
    {
        var label = $"{data}/{size}";
        Console.WriteLine($"[{label}] генерація й побудова BruteForce...");
        var (vectors, queries) = data == "clustered"
            ? ClusteredData.Create(size, QueryCount, topics: Math.Max(20, size / 150), seed: 11)
            : (RandomData.Vectors(size, seed: 1), RandomData.Vectors(QueryCount, seed: 2));
        var exact = new BruteForceIndex(RandomData.Dimensions);
        using var hnsw = new HnswIndex(RandomData.Dimensions, new VectorIndexOptions());
        Fill(exact, vectors);

        var clock = Stopwatch.StartNew();
        Fill(hnsw, vectors);
        Console.WriteLine($"[{label}] HNSW побудовано за {clock.Elapsed.TotalSeconds:0.0} с");

        var truth = queries
            .Select(query => exact.Search(query, K).Select(r => r.ChunkId).ToHashSet())
            .ToArray();

        lines.Add(Row(data, size, "BruteForce", 0, 1.0, MedianLatencyMs(exact, queries)));
        foreach (var ef in EfSearchValues)
        {
            EfSearchField.SetValue(hnsw, ef);
            var recall = MeanRecall(hnsw, queries, truth);
            var latency = MedianLatencyMs(hnsw, queries);
            lines.Add(Row(data, size, "Hnsw", ef, recall, latency));
            Console.WriteLine($"[{label}] efSearch={ef}: recall@{K} = {recall:0.000}, {latency * 1000:0.0} мкс/запит");
        }
    }

    private static void Fill(IVectorIndex index, float[][] vectors)
    {
        for (var i = 0; i < vectors.Length; i++)
        {
            index.Add(new Guid(i, 0, 0, new byte[8]), vectors[i]);
        }
    }

    private static double MeanRecall(HnswIndex index, float[][] queries, HashSet<Guid>[] truth)
    {
        var total = 0d;
        for (var i = 0; i < queries.Length; i++)
        {
            total += index.Search(queries[i], K).Count(r => truth[i].Contains(r.ChunkId)) / (double)K;
        }

        return total / queries.Length;
    }

    /// <summary>Медіана за кількома проходами по всіх запитах (перший прохід — розігрів), мілісекунди на запит.</summary>
    private static double MedianLatencyMs(IVectorIndex index, float[][] queries)
    {
        foreach (var query in queries)
        {
            index.Search(query, K);
        }

        var samples = new double[Passes];
        for (var pass = 0; pass < Passes; pass++)
        {
            var start = Stopwatch.GetTimestamp();
            foreach (var query in queries)
            {
                index.Search(query, K);
            }

            samples[pass] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / queries.Length;
        }

        Array.Sort(samples);
        return samples[Passes / 2];
    }

    private static string Row(string data, int size, string index, int efSearch, double recall, double latencyMs) =>
        string.Create(CultureInfo.InvariantCulture, $"{data};{size};{index};{efSearch};{recall:0.0000};{latencyMs:0.00000}");
}

/// <summary>Вектори з темами: спільний для всіх компонент, центр теми й шум; запити з того самого розподілу.</summary>
internal static class ClusteredData
{
    public static (float[][] Vectors, float[][] Queries) Create(int count, int queryCount, int topics, int seed)
    {
        // Центри тем і спільний компонент залежать лише від seed, тож запити беруться з того ж розподілу.
        var structure = new Random(seed);
        var common = Gaussian(structure);
        var centers = Enumerable.Range(0, topics).Select(_ => Gaussian(structure)).ToArray();
        return (Generate(count, common, centers, new Random(seed + 1000)), Generate(queryCount, common, centers, new Random(seed + 2000)));
    }

    private static float[][] Generate(int count, float[] common, float[][] centers, Random random)
    {
        var result = new float[count][];
        for (var i = 0; i < count; i++)
        {
            var center = centers[random.Next(centers.Length)];
            var noise = Gaussian(random);
            var vector = new float[RandomData.Dimensions];
            var norm = 0d;
            for (var d = 0; d < vector.Length; d++)
            {
                vector[d] = (0.8f * common[d]) + center[d] + (0.5f * noise[d]);
                norm += vector[d] * vector[d];
            }

            var scale = (float)(1d / Math.Sqrt(norm));
            for (var d = 0; d < vector.Length; d++)
            {
                vector[d] *= scale;
            }

            result[i] = vector;
        }

        return result;
    }

    private static float[] Gaussian(Random random)
    {
        var values = new float[RandomData.Dimensions];
        for (var d = 0; d < values.Length; d++)
        {
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            values[d] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        return values;
    }
}
