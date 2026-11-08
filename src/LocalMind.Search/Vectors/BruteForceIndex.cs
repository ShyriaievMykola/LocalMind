using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;

namespace LocalMind.Search.Vectors;

/// <summary>
/// Точний пошук повним перебором усіх векторів. Паралельні пошуки не блокують один одного;
/// додавання і видалення виконуються ексклюзивно.
/// </summary>
public sealed class BruteForceIndex : IVectorIndex, IDisposable
{
    private static readonly Comparer<ScoredChunk> WorstFirst = Comparer<ScoredChunk>.Create((left, right) =>
    {
        var byScore = left.Score.CompareTo(right.Score);
        return byScore != 0 ? byScore : right.ChunkId.CompareTo(left.ChunkId);
    });

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<Guid, float[]> _vectors = [];

    /// <param name="dimensions">Розмірність векторів, більше нуля.</param>
    public BruteForceIndex(int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        Dimensions = dimensions;
    }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _vectors.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc />
    public void Add(Guid chunkId, ReadOnlyMemory<float> vector)
    {
        EnsureDimensions(vector.Length, nameof(vector));
        var copy = vector.ToArray();

        _lock.EnterWriteLock();
        try
        {
            _vectors[chunkId] = copy;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public bool Remove(Guid chunkId)
    {
        _lock.EnterWriteLock();
        try
        {
            return _vectors.Remove(chunkId);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScoredChunk> Search(ReadOnlySpan<float> query, int k)
    {
        EnsureDimensions(query.Length, nameof(query));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        // Купа з k найкращих: на вершині найгірший із них, тож пошук займає O(n log k).
        var best = new PriorityQueue<ScoredChunk, ScoredChunk>(WorstFirst);

        _lock.EnterReadLock();
        try
        {
            foreach (var (chunkId, vector) in _vectors)
            {
                var candidate = new ScoredChunk(chunkId, VectorMath.DotProduct(query, vector));
                if (best.Count < k)
                {
                    best.Enqueue(candidate, candidate);
                }
                else if (WorstFirst.Compare(candidate, best.Peek()) > 0)
                {
                    best.EnqueueDequeue(candidate, candidate);
                }
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        var results = new ScoredChunk[best.Count];
        for (var i = results.Length - 1; i >= 0; i--)
        {
            results[i] = best.Dequeue();
        }

        return results;
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private void EnsureDimensions(int length, string parameterName)
    {
        if (length != Dimensions)
        {
            throw new ArgumentException(
                $"Довжина вектора ({length}) не дорівнює розмірності індексу ({Dimensions}).",
                parameterName);
        }
    }
}
