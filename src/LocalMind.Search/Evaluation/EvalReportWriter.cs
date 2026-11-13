using System.Globalization;
using System.Text;
using LocalMind.Core.Configuration;

namespace LocalMind.Search.Evaluation;

/// <summary>Формати результатів <c>eval</c>: CSV для аналізу й Markdown для звіту. Числа — з крапкою, незалежно від мови ОС.</summary>
public static class EvalReportWriter
{
    private const string TypeAll = "all";

    /// <summary>
    /// CSV у «довгому» вигляді: рядок на кожну пару (прогін, режим, тип); заміри корпусу
    /// (документи, фрагменти, час індексації, розмір БД, пам'ять) і параметри комбінації (модель, нарізка)
    /// повторюються в кожному рядку, щоб файл читався без правил і з нього будувалися графіки E2 і E3.
    /// </summary>
    public static string ToCsv(EvalReport report) => ToCsv([report]);

    /// <summary>CSV для кількох корпусів (комбінацій нарізки й моделі): один заголовок, рядки всіх звітів поспіль.</summary>
    public static string ToCsv(IReadOnlyList<EvalReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var builder = new StringBuilder();
        builder.AppendLine(
            "dataset,run,mode,type,queries,k,recall,mrr,ndcg,latency_p50_ms,latency_p95_ms,documents,chunks,index_seconds,db_bytes,peak_memory_bytes,model,chunking,chunk_size_tokens,overlap_percent");
        foreach (var report in reports)
        {
            foreach (var row in report.Rows)
            {
                builder.AppendLine(string.Join(',',
                    Escape(report.Dataset),
                    Escape(row.Run),
                    row.Mode.ToString().ToLowerInvariant(),
                    TypeName(row.Type),
                    Number(row.Queries),
                    Number(row.K),
                    Number(row.Recall, "0.0000"),
                    Number(row.Mrr, "0.0000"),
                    Number(row.Ndcg, "0.0000"),
                    Number(row.LatencyP50Ms, "0.00"),
                    Number(row.LatencyP95Ms, "0.00"),
                    Number(report.Documents),
                    Number(report.Chunks),
                    Number(report.IndexingTime.TotalSeconds, "0.00"),
                    Number(report.DatabaseBytes),
                    Number(report.PeakMemoryBytes),
                    Escape(report.Setup?.Model ?? string.Empty),
                    report.Setup is null ? string.Empty : ChunkingName(report.Setup.Strategy),
                    report.Setup is null ? string.Empty : Number(report.Setup.ChunkSizeTokens),
                    report.Setup is null ? string.Empty : Number(report.Setup.OverlapPercent)));
            }
        }

        return builder.ToString();
    }

    /// <summary>Markdown: заміри запуску і одна таблиця результатів для всіх прогонів і режимів.</summary>
    public static string ToMarkdown(EvalReport report) => ToMarkdown([report]);

    /// <summary>
    /// Markdown для кількох корпусів: заміри кожної комбінації нарізки й моделі окремим пунктом, далі одна таблиця
    /// з усіма прогонами.
    /// </summary>
    public static string ToMarkdown(IReadOnlyList<EvalReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Оцінка пошуку: {reports[0].Dataset}");
        builder.AppendLine();
        foreach (var report in reports)
        {
            if (reports.Count > 1 || report.Setup is not null)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"## {Describe(report)}");
                builder.AppendLine();
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $"- Документів: {report.Documents}, фрагментів: {report.Chunks}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Індексація: {report.IndexingTime.TotalSeconds:0.0} с");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Розмір бази: {report.DatabaseBytes / 1024.0 / 1024.0:0.00} МБ");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Пікова пам'ять: {report.PeakMemoryBytes / 1024.0 / 1024.0:0} МБ");
            builder.AppendLine();
        }

        builder.AppendLine("| Прогін | Режим | Тип запитів | Запитів | k | Recall@k | MRR | nDCG@k | p50, мс | p95, мс |");
        builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var row in reports.SelectMany(report => report.Rows))
        {
            builder.AppendLine(string.Join(" | ",
                "| " + row.Run.Replace("|", "\\|", StringComparison.Ordinal),
                row.Mode.ToString().ToLowerInvariant(),
                TypeName(row.Type),
                Number(row.Queries),
                Number(row.K),
                Number(row.Recall, "0.000"),
                Number(row.Mrr, "0.000"),
                Number(row.Ndcg, "0.000"),
                Number(row.LatencyP50Ms, "0.0"),
                Number(row.LatencyP95Ms, "0.0") + " |"));
        }

        return builder.ToString();
    }

    private static string Describe(EvalReport report) =>
        report.Setup is { } setup
            ? $"{setup.Model}, {ChunkingName(setup.Strategy)}, фрагмент {setup.ChunkSizeTokens} токенів, перекриття {setup.OverlapPercent}%"
            : "Налаштування за замовчуванням";

    private static string ChunkingName(ChunkingStrategy strategy) =>
        strategy == ChunkingStrategy.FixedSize ? "fixedSize" : "paragraph";

    private static string TypeName(QueryType? type) => type switch
    {
        null => TypeAll,
        QueryType.CrossLingual => "crossLingual",
        var other => other.ToString()!.ToLowerInvariant(),
    };

    private static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.AsSpan().IndexOfAny(",\"\n\r") < 0 ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
