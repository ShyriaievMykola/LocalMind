using LocalMind.Core;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Models;
using LocalMind.Core.Storage;
using LocalMind.Search.Configuration;
using LocalMind.Search.Evaluation;
using LocalMind.TestUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.Search.Tests.Evaluation;

/// <summary>Прогін на маленькому датасеті: справжні SQLite, FTS5 і DI, замість ONNX — <see cref="FakeEmbedder"/>.</summary>
public sealed class EvalRunnerTests : IDisposable
{
    private static readonly SearchMode[] AllModes = [SearchMode.Keyword, SearchMode.Semantic, SearchMode.Hybrid];

    private readonly string _root = Directory.CreateTempSubdirectory("localmind-evalrun-").FullName;
    private readonly string _databasePath;

    public EvalRunnerTests()
    {
        _databasePath = Path.Combine(_root, "db", "eval.db");
        var corpus = Path.Combine(_root, "dataset", "corpus");
        Directory.CreateDirectory(corpus);
        File.WriteAllText(Path.Combine(corpus, "coffee.txt"), "кава еспресо вода температура мелення");
        File.WriteAllText(Path.Combine(corpus, "sleep.txt"), "сон ніч темрява тиша відпочинок");
        File.WriteAllText(Path.Combine(corpus, "git.txt"), "git rebase branch merge commit");
        File.WriteAllText(
            Path.Combine(_root, "dataset", "queries.jsonl"),
            """
            {"id": "q1", "text": "кава еспресо", "type": "exact"}
            {"id": "q2", "text": "відпочинок ніч", "type": "paraphrase"}
            {"id": "q3", "text": "git branch", "type": "crossLingual"}
            {"id": "q4", "text": "rebase", "type": "exact"}
            """);
        File.WriteAllText(
            Path.Combine(_root, "dataset", "qrels.jsonl"),
            """
            {"query": "q1", "document": "coffee.txt", "relevance": 3}
            {"query": "q2", "document": "sleep.txt", "relevance": 3}
            {"query": "q3", "document": "git.txt", "relevance": 3}
            {"query": "q4", "document": "sleep.txt", "relevance": 3}
            """);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task RunAsync_ThreeModes_ProducesRowsWithMetricsAndMeasurements()
    {
        await using var services = await StartAsync();

        var report = await RunAsync(services, [new EvalRun("all", AllModes, 10)]);

        Assert.Equal("dataset", report.Dataset);
        Assert.Equal(3, report.Documents);
        Assert.True(report.Chunks >= 3);
        Assert.True(report.IndexingTime > TimeSpan.Zero);
        Assert.True(report.DatabaseBytes > 0);
        Assert.True(report.PeakMemoryBytes > 0);

        // На кожен режим: «усі» + exact + paraphrase + crossLingual.
        Assert.Equal(12, report.Rows.Count);
        Assert.Equal(AllModes, report.Rows.Select(r => r.Mode).Distinct());
        Assert.All(report.Rows, row =>
        {
            Assert.InRange(row.Recall, 0, 1);
            Assert.InRange(row.Mrr, 0, 1);
            Assert.InRange(row.Ndcg, 0, 1);
            Assert.True(row.LatencyP50Ms >= 0);
            Assert.True(row.LatencyP95Ms >= row.LatencyP50Ms);
        });
    }

    [Fact]
    public async Task RunAsync_Keyword_MetricsMatchHandCheckedOutcome()
    {
        await using var services = await StartAsync();

        var report = await RunAsync(services, [new EvalRun("bm25", [SearchMode.Keyword], 10)]);

        // q1–q3 знаходять свій документ першим, а q4 («rebase») веде в git.txt, тоді як розмічено sleep.txt.
        var all = Assert.Single(report.Rows, r => r.Type is null);
        Assert.Equal(4, all.Queries);
        Assert.Equal(0.75, all.Recall, precision: 10);
        Assert.Equal(0.75, all.Mrr, precision: 10);
        Assert.Equal(0.75, all.Ndcg, precision: 10);

        var exact = Assert.Single(report.Rows, r => r.Type == QueryType.Exact);
        Assert.Equal(2, exact.Queries);
        Assert.Equal(0.5, exact.Recall, precision: 10);
        Assert.Equal(1, Assert.Single(report.Rows, r => r.Type == QueryType.Paraphrase).Recall, precision: 10);
    }

    [Fact]
    public async Task RunAsync_Semantic_ReturnsEveryDocumentWithinDepth()
    {
        await using var services = await StartAsync();

        var report = await RunAsync(services, [new EvalRun("semantic", [SearchMode.Semantic], 10)]);

        // Семантичний пошук віддає найближчих сусідів без порогу, тож у три документи k = 10 вміщує всі.
        Assert.Equal(1, Assert.Single(report.Rows, r => r.Type is null).Recall, precision: 10);
    }

    [Fact]
    public async Task RunAsync_SeveralRuns_AreKeptSeparate()
    {
        await using var services = await StartAsync();

        var report = await RunAsync(
            services,
            [new EvalRun("deep", [SearchMode.Keyword], 10), new EvalRun("shallow", [SearchMode.Keyword], 1)]);

        Assert.Equal(["deep", "shallow"], report.Rows.Select(r => r.Run).Distinct());
        Assert.All(report.Rows.Where(r => r.Run == "shallow"), r => Assert.Equal(1, r.K));
    }

    [Fact]
    public async Task RunAsync_BrokenCorpusFile_Throws()
    {
        File.WriteAllText(Path.Combine(_root, "dataset", "corpus", "broken.pdf"), "це не pdf");
        await using var services = await StartAsync();

        var exception = await Assert.ThrowsAsync<LocalMindException>(() => RunAsync(services, [new EvalRun("x", AllModes, 10)]));

        Assert.Contains("не вдалося проіндексувати", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidRuns))]
    public async Task RunAsync_InvalidRuns_Throw(EvalRun[] runs)
    {
        await using var services = await StartAsync();

        await Assert.ThrowsAsync<LocalMindException>(() => RunAsync(services, runs));
    }

    public static TheoryData<EvalRun[]> InvalidRuns() => new()
    {
        Array.Empty<EvalRun>(),
        new[] { new EvalRun(" ", AllModes, 10) },
        new[] { new EvalRun("x", [], 10) },
        new[] { new EvalRun("x", AllModes, 0) },
        new[] { new EvalRun("x", AllModes, 10), new EvalRun("x", AllModes, 5) },
    };

    [Theory]
    [InlineData(new[] { 5.0 }, 50, 5.0)]
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0 }, 50, 2.0)]
    [InlineData(new[] { 4.0, 1.0, 3.0, 2.0 }, 95, 4.0)]
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 }, 95, 10.0)]
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 }, 50, 5.0)]
    public void Percentile_NearestRank(double[] values, double percentile, double expected)
    {
        Assert.Equal(expected, EvalRunner.Percentile(values, percentile));
    }

    private Task<EvalReport> RunAsync(ServiceProvider services, IReadOnlyList<EvalRun> runs)
    {
        var dataset = EvalDatasetLoader.Load(Path.Combine(_root, "dataset"));
        return EvalRunner.RunAsync(
            dataset,
            services.GetRequiredService<FolderIndexer>(),
            services.GetRequiredService<ISearchService>(),
            runs,
            _databasePath);
    }

    private async Task<ServiceProvider> StartAsync()
    {
        var embedder = new FakeEmbedder();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{StorageOptions.SectionName}:{nameof(StorageOptions.DatabasePath)}"] = _databasePath,
                [$"{ChunkingOptions.SectionName}:{nameof(ChunkingOptions.ChunkSizeTokens)}"] = "32",
                [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelName)}"] = embedder.ModelName,
                [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelPath)}"] = "немає.onnx",
                [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.TokenizerPath)}"] = "немає.json",
                [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.Dimensions)}"] =
                    embedder.Dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .Build();

        var provider = new ServiceCollection()
            .AddSingleton<IEmbedder>(embedder)
            .AddSingleton<ITokenCounter, FakeTokenCounter>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddLocalMindCore(configuration)
            .AddLocalMindSearch(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        await provider.InitializeLocalMindStorageAsync();
        return provider;
    }
}
