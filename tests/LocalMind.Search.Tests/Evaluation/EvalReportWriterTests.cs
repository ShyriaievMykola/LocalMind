using System.Globalization;
using LocalMind.Core.Configuration;
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
            "dataset,run,mode,type,queries,k,recall,mrr,ndcg,latency_p50_ms,latency_p95_ms,documents,chunks,index_seconds,db_bytes,peak_memory_bytes,model,chunking,chunk_size_tokens,overlap_percent",
            lines[0]);
        Assert.Equal("demo,bm25,keyword,all,8,10,0.7500,0.5000,0.6250,1.23,4.50,6,12,1.50,2097152,314572800,,,,", lines[1]);
        Assert.Equal("demo,bm25,keyword,crossLingual,2,10,0.0000,0.0000,0.0000,1.00,2.00,6,12,1.50,2097152,314572800,,,,", lines[2]);
    }

    [Fact]
    public void ToCsv_WithSetup_AddsModelAndChunkingColumns()
    {
        var report = Report with { Setup = new EvalSetup("multilingual-e5-base", ChunkingStrategy.Paragraph, 128, 15) };

        var row = EvalReportWriter.ToCsv(report).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[1];

        Assert.EndsWith(",2097152,314572800,multilingual-e5-base,paragraph,128,15", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ToCsv_SeveralReports_HaveOneHeaderAndOwnMeasurementsPerReport()
    {
        var first = Report with { Setup = new EvalSetup("m1", ChunkingStrategy.FixedSize, 64, 0), Chunks = 30 };
        var second = Report with { Setup = new EvalSetup("m2", ChunkingStrategy.FixedSize, 256, 30), Chunks = 8, Rows = [Report.Rows[0] with { Run = "second" }] };

        var lines = EvalReportWriter.ToCsv([first, second]).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(4, lines.Length);
        Assert.Single(lines, line => line.StartsWith("dataset,", StringComparison.Ordinal));
        Assert.Contains(",6,30,1.50,2097152,314572800,m1,fixedSize,64,0", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("demo,second,", lines[3], StringComparison.Ordinal);
        Assert.EndsWith(",6,8,1.50,2097152,314572800,m2,fixedSize,256,30", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_SeveralReports_DescribeEachSetupAndHaveOneTable()
    {
        var first = Report with { Setup = new EvalSetup("m1", ChunkingStrategy.FixedSize, 64, 0) };
        var second = Report with { Setup = new EvalSetup("m2", ChunkingStrategy.Paragraph, 256, 30), Rows = [Report.Rows[0] with { Run = "second" }] };

        var markdown = EvalReportWriter.ToMarkdown([first, second]);

        Assert.Contains("## m1, fixedSize, фрагмент 64 токенів, перекриття 0%", markdown, StringComparison.Ordinal);
        Assert.Contains("## m2, paragraph, фрагмент 256 токенів, перекриття 30%", markdown, StringComparison.Ordinal);
        Assert.Single(markdown.Split('\n'), line => line.StartsWith("| Прогін", StringComparison.Ordinal));
        Assert.Contains("| second | m2 | paragraph 256/30% | keyword | all |", markdown, StringComparison.Ordinal);
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
        Assert.Contains("| bm25 |  |  | keyword | all | 8 | 10 | 0.750 | 0.500 | 0.625 | 1.2 | 4.5 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| bm25 |  |  | keyword | crossLingual | 2 | 10 | 0.000 | 0.000 | 0.000 | 1.0 | 2.0 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Writers_WithRunOrder_PutRowsInConfigOrderNotGroupOrder()
    {
        // Група 1 містить прогони a і c, група 2 — b; у конфігу вони йдуть a, b, c.
        var group1 = Report with { Rows = [Report.Rows[0] with { Run = "a" }, Report.Rows[0] with { Run = "c" }] };
        var group2 = Report with { Rows = [Report.Rows[0] with { Run = "b" }, Report.Rows[1] with { Run = "b" }] };
        string[] order = ["a", "b", "c"];

        var csvRuns = EvalReportWriter.ToCsv([group1, group2], order)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1).Select(line => line.Split(',')[1]).ToList();
        var markdownRuns = EvalReportWriter.ToMarkdown([group1, group2], order)
            .Split('\n').Where(line => line.StartsWith("| a ", StringComparison.Ordinal) || line.StartsWith("| b ", StringComparison.Ordinal) || line.StartsWith("| c ", StringComparison.Ordinal))
            .Select(line => line[2]).ToList();

        Assert.Equal(["a", "b", "b", "c"], csvRuns);
        Assert.Equal(['a', 'b', 'b', 'c'], markdownRuns);
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
