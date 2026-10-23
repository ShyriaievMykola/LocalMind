using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;

namespace LocalMind.TestUtilities;

/// <summary>
/// Спрощений keyword-індекс поверх <see cref="InMemoryDocumentRepository"/> — як і справжній FTS5,
/// «бачить» фрагменти одразу після збереження. Оцінка — кількість різних слів запиту, що є у фрагменті.
/// </summary>
public sealed class InMemoryKeywordIndex(InMemoryDocumentRepository repository) : IKeywordIndex
{
    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        string query,
        int k,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        cancellationToken.ThrowIfCancellationRequested();

        var terms = TextTokens.Split(query).ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<ScoredChunk> results =
        [
            .. repository.AllChunks
                .Select(c => new ScoredChunk(c.Id, TextTokens.Split(c.Text).Distinct().Count(terms.Contains)))
                .Where(r => r.Score > 0)
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.ChunkId)
                .Take(k),
        ];

        return Task.FromResult(results);
    }
}
