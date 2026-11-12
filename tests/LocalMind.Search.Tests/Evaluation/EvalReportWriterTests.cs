using System.Globalization;
using LocalMind.Core.Models;
using LocalMind.Search.Evaluation;

namespace LocalMind.Search.Tests.Evaluation;

public class EvalReportWriterTests
{
    private static readonly EvalReport Report = new(
        "demo",
        Documents: 6,
        Chunks: 12,
        IndexingTime: TimeSpan.FromSeconds(1.5),
        DatabaseBytes: 2_097_152,
        PeakMemoryBytes: 314_572_800,
        Rows:
        [
            new EvalRow("bm25", SearchMode.Keyword, null, 8, 10, 0.75, 0.5, 0.625, 1.234, 4.5),
            new EvalRow("bm25", SearchMode.Keyword, QueryType.CrossLingual, 2, 10, 0, 0, 0, 1, 2),
        ]);

    [Fact]
    public void ToCsv_HasHeaderAndOneRowPerResult()
    {
        var lines = EvalReportWriter.ToCsv(Report).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(3, lines.Length);
        Assert.Equal(
            "dataset,run,mode,type,queries,k,recall,mrr,ndcg,latency_p50_ms,latency_p95_ms,documents,chunks,index_seconds,db_bytes,peak_memory_bytes",
            lines[0]);
        Assert.Equal("demo,bm25,keyword,all,8,10,0.7500,0.5000,0.6250,1.23,4.50,6,12,1.50,2097152,314572800", lines[1]);
        Assert.Equal("demo,bm25,keyword,crossLingual,2,10,0.0000,0.0000,0.0000,1.00,2.00,6,12,1.50,2097152,314572800", lines[2]);
    }

    [Fact]
    public void ToCsv_NamesWithCommasAndQuotes_AreEscaped()
    {
        var report = Report with { Dataset = "a,\"b\"", Rows = [Report.Rows[0] with { Run = "x,y" }] };

        var row = EvalReportWriter.ToCsv(report).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[1];

        Assert.StartsWith("\"a,\"\"b\"\"\",\"x,y\",keyword,", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_HasSummaryAndTableWithAllRows()
    {
        var markdown = EvalReportWriter.ToMarkdown(Report);

        Assert.Contains("# Оцінка пошуку: demo", markdown, StringComparison.Ordinal);
        Assert.Contains("Документів: 6, фрагментів: 12", markdown, StringComparison.Ordinal);
        Assert.Contains("Індексація: 1.5 с", markdown, StringComparison.Ordinal);
        Assert.Contains("Розмір бази: 2.00 МБ", markdown, StringComparison.Ordinal);
        Assert.Contains("Пікова пам'ять: 300 МБ", markdown, StringComparison.Ordinal);
        Assert.Contains("| bm25 | keyword | all | 8 | 10 | 0.750 | 0.500 | 0.625 | 1.2 | 4.5 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| bm25 | keyword | crossLingual | 2 | 10 | 0.000 | 0.000 | 0.000 | 1.0 | 2.0 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Writers_UseInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("uk-UA");
        try
        {
            Assert.Contains("0.7500", EvalReportWriter.ToCsv(Report), StringComparison.Ordinal);
            Assert.Contains("0.750", EvalReportWriter.ToMarkdown(Report), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
