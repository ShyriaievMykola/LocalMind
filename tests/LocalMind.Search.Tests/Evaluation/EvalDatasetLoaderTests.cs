using LocalMind.Core.Errors;
using LocalMind.Search.Evaluation;

namespace LocalMind.Search.Tests.Evaluation;

public sealed class EvalDatasetLoaderTests : IDisposable
{
    private const string GoodQueries = """
        {"id": "q1", "text": "кава", "type": "exact"}
        {"id": "q2", "text": "how to sleep", "type": "crossLingual"}
        """;

    private const string GoodQrels = """
        {"query": "q1", "document": "coffee.md", "relevance": 3}
        {"query": "q2", "document": "notes/sleep.md", "relevance": 2}
        {"query": "q2", "document": "coffee.md", "relevance": 1}
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("localmind-eval-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Load_ValidDataset_ReadsQueriesAndRelevance()
    {
        CreateDataset(GoodQueries, GoodQrels);

        var dataset = EvalDatasetLoader.Load(_root);

        Assert.Equal(["q1", "q2"], dataset.Queries.Select(q => q.Id));
        Assert.Equal(QueryType.CrossLingual, dataset.Queries[1].Type);
        Assert.Equal(3, dataset.Relevance["q1"]["coffee.md"]);
        Assert.Equal(2, dataset.Relevance["q2"]["notes/sleep.md"]);
        Assert.Equal(Path.Combine(_root, "corpus"), dataset.CorpusDirectory);
    }

    [Fact]
    public void Load_BlankLinesAndBackslashPaths_AreAccepted()
    {
        CreateDataset(
            GoodQueries + "\n\n",
            GoodQrels.Replace("notes/sleep.md", @"notes\\sleep.md", StringComparison.Ordinal));

        var dataset = EvalDatasetLoader.Load(_root);

        Assert.True(dataset.Relevance["q2"].ContainsKey("notes/sleep.md"));
    }

    [Fact]
    public void Load_DemoDataset_IsValid()
    {
        var dataset = EvalDatasetLoader.Load(FindDemoDataset());

        Assert.NotEmpty(dataset.Queries);
        Assert.Contains(dataset.Queries, q => q.Type == QueryType.Exact);
        Assert.Contains(dataset.Queries, q => q.Type == QueryType.Paraphrase);
        Assert.Contains(dataset.Queries, q => q.Type == QueryType.CrossLingual);
    }

    [Theory]
    [InlineData("""{"id": "", "text": "кава", "type": "exact"}""", "queries.jsonl, рядок 1", "порожній id")]
    [InlineData("""{"id": "q1", "text": "  ", "type": "exact"}""", "рядок 1", "без тексту")]
    [InlineData("""{"id": "q1", "text": "кава"}""", "рядок 1", "type")]
    [InlineData("""{"id": "q1", "text": "кава", "type": "fuzzy"}""", "рядок 1", "некоректний JSON")]
    [InlineData("""{"id": "q1", "text": "кава", "type": "exact"}""" + "\n" + """{"id": "q1", "text": "чай", "type": "exact"}""", "рядок 2", "дубльований id")]
    [InlineData("{\"id\": \"q1\", \"text\": \"кава\", \"type\": \"exact\"}\n{не json", "рядок 2", "некоректний JSON")]
    [InlineData("", "queries.jsonl", "немає жодного запиту")]
    public void Load_BrokenQueries_ThrowsWithLineAndReason(string queries, string where, string reason)
    {
        CreateDataset(queries, GoodQrels);

        var exception = Assert.Throws<LocalMindException>(() => EvalDatasetLoader.Load(_root));

        Assert.Contains(where, exception.Message, StringComparison.Ordinal);
        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"query": "q9", "document": "coffee.md", "relevance": 3}""", "невідомий запит q9")]
    [InlineData("""{"query": "q1", "document": "tea.md", "relevance": 3}""", "tea.md")]
    [InlineData("""{"query": "q1", "document": "", "relevance": 3}""", "порожній document")]
    [InlineData("""{"query": "q1", "document": "coffee.md", "relevance": 0}""", "relevance")]
    [InlineData("""{"query": "q1", "document": "coffee.md", "relevance": 4}""", "relevance")]
    [InlineData("""{"query": "q1", "document": "coffee.md"}""", "relevance")]
    [InlineData("{\"query\": \"q1\", \"document\": \"coffee.md\", \"relevance\": 3}\n{\"query\": \"q1\", \"document\": \"coffee.md\", \"relevance\": 2}", "уже розмічено")]
    public void Load_BrokenQrels_ThrowsWithReason(string qrels, string reason)
    {
        CreateDataset(GoodQueries, qrels);

        var exception = Assert.Throws<LocalMindException>(() => EvalDatasetLoader.Load(_root));

        Assert.Contains("qrels.jsonl", exception.Message, StringComparison.Ordinal);
        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_QueryWithoutRelevantDocuments_Throws()
    {
        CreateDataset(GoodQueries, """{"query": "q1", "document": "coffee.md", "relevance": 3}""");

        var exception = Assert.Throws<LocalMindException>(() => EvalDatasetLoader.Load(_root));

        Assert.Contains("q2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingCorpusFolder_Throws()
    {
        File.WriteAllText(Path.Combine(_root, "queries.jsonl"), GoodQueries);
        File.WriteAllText(Path.Combine(_root, "qrels.jsonl"), GoodQrels);

        var exception = Assert.Throws<LocalMindException>(() => EvalDatasetLoader.Load(_root));

        Assert.Contains("corpus", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingQrelsFile_Throws()
    {
        CreateDataset(GoodQueries, GoodQrels);
        File.Delete(Path.Combine(_root, "qrels.jsonl"));

        var exception = Assert.Throws<LocalMindException>(() => EvalDatasetLoader.Load(_root));

        Assert.Contains("qrels.jsonl", exception.Message, StringComparison.Ordinal);
    }

    private void CreateDataset(string queries, string qrels)
    {
        Directory.CreateDirectory(Path.Combine(_root, "corpus", "notes"));
        File.WriteAllText(Path.Combine(_root, "corpus", "coffee.md"), "кава");
        File.WriteAllText(Path.Combine(_root, "corpus", "notes", "sleep.md"), "сон");
        File.WriteAllText(Path.Combine(_root, "queries.jsonl"), queries);
        File.WriteAllText(Path.Combine(_root, "qrels.jsonl"), qrels);
    }

    private static string FindDemoDataset()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var demo = Path.Combine(directory.FullName, "eval", "datasets", "demo");
            if (Directory.Exists(demo))
            {
                return demo;
            }
        }

        throw new DirectoryNotFoundException("eval/datasets/demo не знайдено над тестовою текою.");
    }
}
