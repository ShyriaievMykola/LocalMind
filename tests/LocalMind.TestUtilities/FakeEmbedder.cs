using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;

namespace LocalMind.TestUtilities;

/// <summary>
/// Детермінований embedder без моделі: «мішок слів» — кожне слово хешується в один з вимірів вектора,
/// після чого вектор L2-нормалізується. Тексти зі спільними словами отримують близькі вектори,
/// тому фейк придатний і для тестів пошуку. Однаковий текст завжди дає однаковий вектор.
/// </summary>
public sealed class FakeEmbedder(int dimensions = 64, string modelName = "fake-bag-of-words") : IEmbedder
{
    private readonly List<int> _batchSizes = [];

    public string ModelName { get; } = modelName;

    public int Dimensions { get; } = dimensions > 0
        ? dimensions
        : throw new ArgumentOutOfRangeException(nameof(dimensions));

    public int MaxTokens { get; init; } = 512;

    /// <summary>Розміри наборів текстів, переданих у кожен виклик <see cref="EmbedAsync"/>.</summary>
    public IReadOnlyList<int> Calls => _batchSizes;

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        cancellationToken.ThrowIfCancellationRequested();

        if (texts.Any(t => t is null))
        {
            throw new ArgumentException("Серед текстів є null.", nameof(texts));
        }

        _batchSizes.Add(texts.Count);
        IReadOnlyList<ReadOnlyMemory<float>> vectors = [.. texts.Select(t => (ReadOnlyMemory<float>)Embed(t))];
        return Task.FromResult(vectors);
    }

    /// <summary>Вектор одного тексту; порожній текст дає одиничний вектор уздовж першого виміру.</summary>
    public float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var word in TextTokens.Split(text))
        {
            vector[StableHash(word) % (uint)Dimensions] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        if (norm == 0f)
        {
            vector[0] = 1f;
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    /// <summary>FNV-1a: на відміну від <see cref="string.GetHashCode()"/>, однаковий між запусками.</summary>
    private static uint StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }
}
