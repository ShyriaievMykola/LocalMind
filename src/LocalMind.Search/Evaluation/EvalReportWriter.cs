using System.Globalization;
using System.Text;

namespace LocalMind.Search.Evaluation;

/// <summary>Формати результатів <c>eval</c>: CSV для аналізу й Markdown для звіту. Числа — з крапкою, незалежно від мови ОС.</summary>
public static class EvalReportWriter
{
    private const string TypeAll = "all";

    /// <summary>
    /// CSV у «довгому» вигляді: рядок на кожну пару (прогін, режим, тип); заміри всього запуску
    /// (документи, час індексації, розмір БД, пам'ять) повторюються в кожному рядку, щоб файл читався без правил.
    /// </summary>
    public static string ToCsv(EvalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine(
            "dataset,run,mode,type,queries,k,recall,mrr,ndcg,latency_p50_ms,latency_p95_ms,documents,chunks,index_seconds,db_bytes,peak_memory_bytes");
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
                Number(report.PeakMemoryBytes)));
        }

        return builder.ToString();
    }

    /// <summary>Markdown: заміри запуску і одна таблиця результатів для всіх прогонів і режимів.</summary>
    public static string ToMarkdown(EvalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Оцінка пошуку: {report.Dataset}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Документів: {report.Documents}, фрагментів: {report.Chunks}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Індексація: {report.IndexingTime.TotalSeconds:0.0} с");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Розмір бази: {report.DatabaseBytes / 1024.0 / 1024.0:0.00} МБ");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Пікова пам'ять: {report.PeakMemoryBytes / 1024.0 / 1024.0:0} МБ");
        builder.AppendLine();
        builder.AppendLine("| Прогін | Режим | Тип запитів | Запитів | k | Recall@k | MRR | nDCG@k | p50, мс | p95, мс |");
        builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var row in report.Rows)
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
