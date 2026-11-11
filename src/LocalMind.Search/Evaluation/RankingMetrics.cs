namespace LocalMind.Search.Evaluation;

/// <summary>
/// Метрики якості ранжування за ранжованим списком документів і розміткою релевантності.
/// Список — документи в порядку відповіді, <b>без повторів</b> (фрагменти одного документа згортає викликач:
/// береться перша поява). Розмітка — документ → оцінка 1…3; відсутній документ нерелевантний.
/// </summary>
public static class RankingMetrics
{
    /// <summary>Частка релевантних документів, що потрапили в перші <paramref name="k"/>.</summary>
    public static double RecallAtK(IReadOnlyList<string> ranked, IReadOnlyDictionary<string, int> relevant, int k)
    {
        Validate(ranked, relevant, k);

        var found = ranked.Take(k).Count(relevant.ContainsKey);
        return (double)found / relevant.Count;
    }

    /// <summary>Обернений ранг першого релевантного документа в перших <paramref name="k"/>; 0, якщо його там немає.</summary>
    public static double ReciprocalRank(IReadOnlyList<string> ranked, IReadOnlyDictionary<string, int> relevant, int k)
    {
        Validate(ranked, relevant, k);

        for (var i = 0; i < Math.Min(k, ranked.Count); i++)
        {
            if (relevant.ContainsKey(ranked[i]))
            {
                return 1.0 / (i + 1);
            }
        }

        return 0;
    }

    /// <summary>
    /// nDCG@k із лінійним виграшем: DCG = Σ rel / log₂(ранг + 1), нормований на ідеальний порядок
    /// (найрелевантніші першими).
    /// </summary>
    public static double NdcgAtK(IReadOnlyList<string> ranked, IReadOnlyDictionary<string, int> relevant, int k)
    {
        Validate(ranked, relevant, k);

        var dcg = ranked.Take(k)
            .Select((document, i) => relevant.GetValueOrDefault(document) / Math.Log2(i + 2))
            .Sum();
        var ideal = relevant.Values.OrderDescending().Take(k)
            .Select((gain, i) => gain / Math.Log2(i + 2))
            .Sum();

        return dcg / ideal;
    }

    private static void Validate(IReadOnlyList<string> ranked, IReadOnlyDictionary<string, int> relevant, int k)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        ArgumentNullException.ThrowIfNull(relevant);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        if (relevant.Count == 0)
        {
            throw new ArgumentException("Для запиту немає релевантних документів.", nameof(relevant));
        }

        if (ranked.Distinct(StringComparer.Ordinal).Count() != ranked.Count)
        {
            throw new ArgumentException("Список містить повтори документів.", nameof(ranked));
        }
    }
}
