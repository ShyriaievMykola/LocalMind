using BenchmarkDotNet.Attributes;
using LocalMind.Core.Abstractions;
using LocalMind.Search.Configuration;
using LocalMind.Search.Vectors;

namespace LocalMind.Benchmarks;

/// <summary>Спільні випадкові дані: L2-нормалізовані вектори розмірності моделі (384), фіксоване зерно.</summary>
internal static class RandomData
{
    public const int Dimensions = 384;

    public static float[][] Vectors(int count, int seed)
    {
        var random = new Random(seed);
        var result = new float[count][];
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

            result[i] = vector;
        }

        return result;
    }
}

/// <summary>Скалярний добуток двох векторів розмірності 384 (SIMD) проти простого циклу.</summary>
[MemoryDiagnoser]
public class VectorMathBenchmarks
{
    private float[] _left = [];
    private float[] _right = [];

    [GlobalSetup]
    public void Setup()
    {
        var vectors = RandomData.Vectors(2, seed: 1);
        _left = vectors[0];
        _right = vectors[1];
    }

    [Benchmark(Baseline = true)]
    public float ScalarLoop()
    {
        var sum = 0f;
        for (var i = 0; i < _left.Length; i++)
        {
            sum += _left[i] * _right[i];
        }

        return sum;
    }

    [Benchmark]
    public float VectorMathDotProduct() => VectorMath.DotProduct(_left, _right);
}

/// <summary>Пошук top-10: повний перебір проти HNSW на 1k / 10k / 100k векторів.</summary>
[MemoryDiagnoser]
public class SearchBenchmarks
{
    private const int QueryCount = 100;

    private IVectorIndex _index = null!;
    private float[][] _queries = [];

    [Params(1_000, 10_000, 100_000)]
    public int Size { get; set; }

    [Params(VectorIndexKind.BruteForce, VectorIndexKind.Hnsw)]
    public VectorIndexKind Kind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _index = IndexFactory.Create(Kind);
        var vectors = RandomData.Vectors(Size, seed: 1);
        for (var i = 0; i < vectors.Length; i++)
        {
            _index.Add(new Guid(i, 0, 0, new byte[8]), vectors[i]);
        }

        _queries = RandomData.Vectors(QueryCount, seed: 2);
    }

    [GlobalCleanup]
    public void Cleanup() => (_index as IDisposable)?.Dispose();

    /// <summary>Один запуск — сто запитів, тож час на запит — результат, поділений на 100.</summary>
    [Benchmark(OperationsPerInvoke = QueryCount)]
    public int Search()
    {
        var found = 0;
        foreach (var query in _queries)
        {
            found += _index.Search(query, 10).Count;
        }

        return found;
    }
}

/// <summary>Побудова індексу (додавання всіх векторів): 1k / 10k; на 100k HNSW будується надто довго для повторів.</summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1, invocationCount: 1)]
public class BuildBenchmarks
{
    private float[][] _vectors = [];

    [Params(1_000, 10_000)]
    public int Size { get; set; }

    [Params(VectorIndexKind.BruteForce, VectorIndexKind.Hnsw)]
    public VectorIndexKind Kind { get; set; }

    [GlobalSetup]
    public void Setup() => _vectors = RandomData.Vectors(Size, seed: 1);

    [Benchmark]
    public int Build()
    {
        var index = IndexFactory.Create(Kind);
        for (var i = 0; i < _vectors.Length; i++)
        {
            index.Add(new Guid(i, 0, 0, new byte[8]), _vectors[i]);
        }

        (index as IDisposable)?.Dispose();
        return _vectors.Length;
    }
}

internal static class IndexFactory
{
    public static IVectorIndex Create(VectorIndexKind kind) => kind == VectorIndexKind.Hnsw
        ? new HnswIndex(RandomData.Dimensions, new VectorIndexOptions())
        : new BruteForceIndex(RandomData.Dimensions);
}
