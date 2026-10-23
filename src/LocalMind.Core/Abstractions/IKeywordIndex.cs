using LocalMind.Core.Models;

namespace LocalMind.Core.Abstractions;

/// <summary>
/// Повнотекстовий пошук із ранжуванням BM25 (SQLite FTS5). Індекс оновлюється автоматично
/// під час збереження фрагментів через <see cref="IDocumentRepository"/>, тому методів додавання тут немає.
/// </summary>
public interface IKeywordIndex
{
    /// <summary>Шукає фрагменти за ключовими словами.</summary>
    /// <param name="query">
    /// Текст запиту як його ввів користувач. Спецсимволи синтаксису FTS5 (<c>"</c>, <c>*</c>, <c>OR</c> …)
    /// екрануються реалізацією і не можуть зламати запит.
    /// </param>
    /// <param name="k">Скільки результатів повернути, більше нуля.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>До <paramref name="k"/> результатів, відсортованих за спаданням релевантності.</returns>
    /// <exception cref="ArgumentException">Порожній запит або <paramref name="k"/> ≤ 0.</exception>
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(string query, int k, CancellationToken cancellationToken = default);
}
