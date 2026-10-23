using LocalMind.Core.Models;

namespace LocalMind.Core.Abstractions;

/// <summary>Єдина точка входу в пошук для застосунку, CLI та eval-раннера.</summary>
public interface ISearchService
{
    /// <summary>Виконує пошук у вказаному в запиті режимі.</summary>
    /// <param name="query">Запит; вже провалідований під час створення.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>
    /// До <see cref="SearchQuery.TopK"/> результатів, відсортованих за спаданням <see cref="SearchResult.Score"/>.
    /// Якщо нічого не знайдено — порожній список.
    /// </returns>
    /// <exception cref="OperationCanceledException">Операцію скасовано.</exception>
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}
