using LocalMind.Core.Models;

namespace LocalMind.Search.Hybrid;

/// <summary>
/// Reciprocal Rank Fusion (DR-005): зливає кілька ранжованих списків за рангами, а не за оцінками,
/// тому різні шкали (BM25, косинус) не треба нормалізувати. Оцінка фрагмента — Σ 1 / (k + rank), rank від 1.
/// </summary>
public static class ReciprocalRankFusion
{
    /// <summary>Типове значення k з оригінальної статті (Cormack et al., 2009).</summary>
    public const int DefaultK = 60;

    /// <summary>Зливає списки в один.</summary>
    /// <param name="rankings">Списки, кожен відсортований від найкращого; повтор фрагмента в одному списку не враховується.</param>
    /// <param name="k">Згладжувальна константа, більше нуля: що більша, то менше важить верх списку.</param>
    /// <param name="topK">Скільки результатів повернути, більше нуля.</param>
    /// <returns>
    /// До <paramref name="topK"/> фрагментів за спаданням RRF-оцінки; за рівності — вище той, чий найкращий ранг кращий.
    /// </returns>
    public static IReadOnlyList<ScoredChunk> Fuse(IReadOnlyList<IReadOnlyList<ScoredChunk>> rankings, int k, int topK)
    {
        ArgumentNullException.ThrowIfNull(rankings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var fused = new Dictionary<Guid, (double Score, int BestRank)>();
        foreach (var ranking in rankings)
        {
            ArgumentNullException.ThrowIfNull(ranking, nameof(rankings));

            var seen = new HashSet<Guid>();
            var rank = 0;
            foreach (var (chunkId, _) in ranking)
            {
                if (!seen.Add(chunkId))
                {
                    continue;
                }

                rank++;
                var current = fused.GetValueOrDefault(chunkId, (0, int.MaxValue));
                fused[chunkId] = (current.Score + 1.0 / (k + rank), Math.Min(current.BestRank, rank));
            }
        }

        return
        [
            .. fused
                .OrderByDescending(entry => entry.Value.Score)
                .ThenBy(entry => entry.Value.BestRank)
                .ThenBy(entry => entry.Key)
                .Take(topK)
                .Select(entry => new ScoredChunk(entry.Key, entry.Value.Score)),
        ];
    }
}
