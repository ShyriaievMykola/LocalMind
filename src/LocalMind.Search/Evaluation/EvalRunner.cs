using System.Diagnostics;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Models;

namespace LocalMind.Search.Evaluation;

/// <summary>
/// Індексує корпус датасету, проганяє запити в заданих режимах і рахує метрики та час відповіді.
/// Працює з тим сховищем, яке отримала через <see cref="FolderIndexer"/> і <see cref="ISearchService"/>:
/// викликач відповідає за те, щоб це була окрема тимчасова база, а не бібліотека користувача.
/// </summary>
public static class EvalRunner
{
    /// <summary>
    /// Скільки фрагментів просити в пошуку на кожен документ у метриках: кілька фрагментів одного документа
    /// згортаються в один, тож для k документів потрібно більше k фрагментів.
    /// </summary>
    internal const int ChunksPerDocument = 5;

    /// <summary>Проганяє датасет.</summary>
    /// <param name="dataset">Датасет.</param>
    /// <param name="indexer">Індексатор тимчасової бази.</param>
    /// <param name="search">Пошук по тій самій базі.</param>
    /// <param name="runs">Прогони (режими й k).</param>
    /// <param name="databasePath">Файл бази, щоб виміряти її розмір; може бути <see langword="null"/>.</param>
    /// <param name="cancellationToken">Скасування.</param>
    /// <exception cref="LocalMindException">Корпус не проіндексувався або прогони задано некоректно.</exception>
    public static async Task<EvalReport> RunAsync(
        EvalDataset dataset,
        FolderIndexer indexer,
        ISearchService search,
        IReadOnlyList<EvalRun> runs,
        string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(search);
        ValidateRuns(runs);

        var stopwatch = Stopwatch.StartNew();
        var indexing = await indexer.IndexAsync(dataset.CorpusDirectory, cancellationToken: cancellationToken);
        stopwatch.Stop();
        if (indexing.Failed > 0)
        {
            throw new LocalMindException(
                $"Корпус {dataset.CorpusDirectory}: не вдалося проіндексувати файлів — {indexing.Failed}. " +
                "Перевірте їх командою index.");
        }

        var corpus = Path.GetFullPath(dataset.CorpusDirectory);
        var rows = new List<EvalRow>();
        foreach (var run in runs)
        {
            foreach (var mode in run.Modes)
            {
                rows.AddRange(await RunModeAsync(dataset, search, corpus, run, mode, cancellationToken));
            }
        }

        return new EvalReport(
            new DirectoryInfo(Path.GetDirectoryName(corpus)!).Name,
            indexing.Indexed + indexing.Unchanged,
            indexing.Chunks,
            stopwatch.Elapsed,
            DatabaseSize(databasePath),
            Process.GetCurrentProcess().PeakWorkingSet64,
            rows);
    }

    /// <summary>Перцентиль методом найближчого рангу; <paramref name="values"/> не порожній.</summary>
    internal static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var sorted = values.Order().ToList();
        var index = (int)Math.Ceiling(percentile / 100 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static async Task<List<EvalRow>> RunModeAsync(
        EvalDataset dataset,
        ISearchService search,
        string corpus,
        EvalRun run,
        SearchMode mode,
        CancellationToken cancellationToken)
    {
        var topK = run.K * ChunksPerDocument;

        // Перший запит у режимі завантажує вектори й модель; в заміри часу він не входить.
        await search.SearchAsync(new SearchQuery(dataset.Queries[0].Text, mode, topK), cancellationToken);

        var measurements = new List<Measurement>(dataset.Queries.Count);
        foreach (var query in dataset.Queries)
        {
            var started = Stopwatch.GetTimestamp();
            var results = await search.SearchAsync(new SearchQuery(query.Text, mode, topK), cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            var ranked = FoldToDocuments(results, corpus);
            var relevant = dataset.Relevance[query.Id];
            measurements.Add(new Measurement(
                query.Type,
                RankingMetrics.RecallAtK(ranked, relevant, run.K),
                RankingMetrics.ReciprocalRank(ranked, relevant, run.K),
                RankingMetrics.NdcgAtK(ranked, relevant, run.K),
                elapsed));
        }

        var rows = new List<EvalRow> { Summarize(run, mode, null, measurements) };
        foreach (var type in Enum.GetValues<QueryType>())
        {
            var ofType = measurements.Where(m => m.Type == type).ToList();
            if (ofType.Count > 0)
            {
                rows.Add(Summarize(run, mode, type, ofType));
            }
        }

        return rows;
    }

    private static EvalRow Summarize(EvalRun run, SearchMode mode, QueryType? type, List<Measurement> measurements)
    {
        var latencies = measurements.Select(m => m.LatencyMs).ToList();
        return new EvalRow(
            run.Name,
            mode,
            type,
            measurements.Count,
            run.K,
            measurements.Average(m => m.Recall),
            measurements.Average(m => m.ReciprocalRank),
            measurements.Average(m => m.Ndcg),
            Percentile(latencies, 50),
            Percentile(latencies, 95));
    }

    /// <summary>Документи в порядку відповіді, відносно корпусу; повтори (інші фрагменти) відкидаються.</summary>
    private static List<string> FoldToDocuments(IReadOnlyList<SearchResult> results, string corpus) =>
        results
            .Select(r => Path.GetRelativePath(corpus, r.DocumentPath).Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static long DatabaseSize(string? databasePath)
    {
        if (databasePath is null)
        {
            return 0;
        }

        // У режимі WAL частина даних ще може лежати в журналі.
        return new[] { databasePath, databasePath + "-wal" }
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);
    }

    private static void ValidateRuns(IReadOnlyList<EvalRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Count == 0)
        {
            throw new LocalMindException("Не задано жодного прогону.");
        }

        foreach (var run in runs)
        {
            if (string.IsNullOrWhiteSpace(run.Name))
            {
                throw new LocalMindException("Прогін без назви.");
            }

            if (run.Modes.Count == 0)
            {
                throw new LocalMindException($"Прогін {run.Name}: не задано жодного режиму.");
            }

            if (run.K <= 0)
            {
                throw new LocalMindException($"Прогін {run.Name}: k має бути більше 0.");
            }
        }

        var duplicate = runs.GroupBy(r => r.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new LocalMindException($"Назва прогону {duplicate.Key} повторюється.");
        }
    }

    private sealed record Measurement(QueryType Type, double Recall, double ReciprocalRank, double Ndcg, double LatencyMs);
}
