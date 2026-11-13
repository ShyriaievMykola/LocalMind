using LocalMind.Core.Models;

namespace LocalMind.Core.Abstractions;

/// <summary>
/// Індекс для пошуку найближчих векторів у пам'яті (brute-force або HNSW — експеримент E4).
/// Реалізації потокобезпечні: пошук може виконуватися паралельно з додаванням і видаленням.
/// </summary>
public interface IVectorIndex
{
    /// <summary>Розмірність векторів, які приймає індекс.</summary>
    int Dimensions { get; }

    /// <summary>Кількість векторів в індексі.</summary>
    int Count { get; }

    /// <summary>Додає вектор фрагмента; якщо фрагмент уже є — замінює його вектор.</summary>
    /// <exception cref="ArgumentException">Довжина вектора не дорівнює <see cref="Dimensions"/>.</exception>
    void Add(Guid chunkId, ReadOnlyMemory<float> vector);

    /// <summary>Видаляє вектор фрагмента.</summary>
    /// <returns><see langword="true"/>, якщо фрагмент був в індексі.</returns>
    bool Remove(Guid chunkId);

    /// <summary>
    /// Видаляє кілька векторів. Реалізації можуть робити це дешевше, ніж серію <see cref="Remove"/>
    /// (HNSW — одним проходом по графі); за замовчуванням — просто серія.
    /// </summary>
    /// <returns>Скільки векторів було в індексі й видалено.</returns>
    int RemoveRange(IEnumerable<Guid> chunkIds)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);

        var removed = 0;
        foreach (var chunkId in chunkIds)
        {
            if (Remove(chunkId))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Шукає фрагменти, найближчі до вектора запиту.</summary>
    /// <param name="query">L2-нормалізований вектор запиту.</param>
    /// <param name="k">Скільки результатів повернути, більше нуля.</param>
    /// <returns>
    /// До <paramref name="k"/> результатів, відсортованих за спаданням косинусної схожості
    /// (<see cref="ScoredChunk.Score"/> від −1 до 1). Для порожнього індексу — порожній список.
    /// </returns>
    /// <exception cref="ArgumentException">Неправильна довжина вектора або <paramref name="k"/> ≤ 0.</exception>
    IReadOnlyList<ScoredChunk> Search(ReadOnlySpan<float> query, int k);
}
