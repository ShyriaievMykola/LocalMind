using LocalMind.Core;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using LocalMind.Core.Storage;
using LocalMind.Search.Configuration;
using LocalMind.TestUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.Search.Tests.EndToEnd;

/// <summary>
/// Наскрізний сценарій без моделі: справжні парсери, нарізка, SQLite на диску і DI як у хості,
/// замість ONNX — <see cref="FakeEmbedder"/>.
/// </summary>
public sealed class IndexThenSearchTests : IDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(20);

    private readonly string _root = Directory.CreateTempSubdirectory("localmind-e2e-").FullName;
    private readonly string _notes;
    private readonly string _databasePath;

    public IndexThenSearchTests()
    {
        _notes = Path.Combine(_root, "нотатки");
        _databasePath = Path.Combine(_root, "db", "localmind.db");
        Directory.CreateDirectory(_notes);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AddFolder_MixedFormatsWithBrokenFiles_EverythingSupportedIsSearchable()
    {
        WriteSampleLibrary();
        await using var host = await StartAsync();

        await host.Indexing.AddFolderAsync(_notes);
        await WaitCompletedAsync(host.Indexing);

        var documents = (await host.Repository.GetDocumentsAsync()).ToDictionary(d => Path.GetFileName(d.Path));
        Assert.Equal(
            ["broken.docx", "broken.pdf", "coffee.txt", "empty.txt", "plan.docx", "report.pdf", "scan.pdf", "подорож.md"],
            documents.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(DocumentStatus.Error, documents["broken.pdf"].Status);
        Assert.Equal(DocumentStatus.Error, documents["broken.docx"].Status);
        Assert.False(string.IsNullOrWhiteSpace(documents["broken.pdf"].Error));
        Assert.Equal(DocumentStatus.Empty, documents["empty.txt"].Status);
        Assert.Equal(DocumentStatus.NoText, documents["scan.pdf"].Status);
        foreach (var name in (string[])["coffee.txt", "plan.docx", "report.pdf", "подорож.md"])
        {
            Assert.Equal(DocumentStatus.Indexed, documents[name].Status);
        }

        Assert.Equal(2, host.Indexing.Progress.Failed);

        Assert.Equal("coffee.txt", await TopFileAsync(host, "кава зранку"));
        Assert.Equal("подорож.md", await TopFileAsync(host, "квитки на потяг до Львова"));
        Assert.Equal("plan.docx", await TopFileAsync(host, "Звичайний абзац"));
        Assert.Equal("report.pdf", await TopFileAsync(host, "Hello World"));
    }

    [Fact]
    public async Task Restart_SearchUsesStoredVectorsAndUnchangedFilesAreNotReembedded()
    {
        WriteSampleLibrary();
        await using (var first = await StartAsync())
        {
            await first.Indexing.AddFolderAsync(_notes);
            await WaitCompletedAsync(first.Indexing);
        }

        await using var second = await StartAsync();
        await second.Indexing.RescanAsync();
        await WaitCompletedAsync(second.Indexing);

        Assert.Empty(second.Embedder.Calls);
        Assert.Equal("coffee.txt", await TopFileAsync(second, "кава зранку"));
    }

    [Fact]
    public async Task Rescan_AfterChangesWhileStopped_ReindexesChangedAndDropsDeleted()
    {
        WriteSampleLibrary();
        await using (var first = await StartAsync())
        {
            await first.Indexing.AddFolderAsync(_notes);
            await WaitCompletedAsync(first.Indexing);
        }

        File.WriteAllText(Path.Combine(_notes, "coffee.txt"), "зелений чай з м'ятою");
        File.Delete(Path.Combine(_notes, "report.pdf"));

        await using var second = await StartAsync();
        await second.Indexing.RescanAsync();
        await WaitCompletedAsync(second.Indexing);

        var paths = (await second.Repository.GetDocumentsAsync()).Select(d => Path.GetFileName(d.Path)).ToList();
        Assert.DoesNotContain("report.pdf", paths);
        Assert.Equal("coffee.txt", await TopFileAsync(second, "зелений чай"));

        var oldText = await second.Search.SearchAsync(new SearchQuery("кава зранку", SearchMode.Semantic, topK: 50));
        Assert.DoesNotContain(oldText, r => r.Chunk.Text.Contains("кава", StringComparison.Ordinal));
        Assert.DoesNotContain(oldText, r => r.DocumentPath.EndsWith("report.pdf", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveFolder_ClearsLibraryAndSearch()
    {
        WriteSampleLibrary();
        await using var host = await StartAsync();
        await host.Indexing.AddFolderAsync(_notes);
        await WaitCompletedAsync(host.Indexing);

        await host.Indexing.RemoveFolderAsync(_notes);

        Assert.Empty(await host.Repository.GetDocumentsAsync());
        Assert.Empty(await host.Indexing.GetFoldersAsync());
        Assert.Empty(await host.Search.SearchAsync(new SearchQuery("кава", SearchMode.Semantic)));
    }

    private void WriteSampleLibrary()
    {
        File.WriteAllText(Path.Combine(_notes, "coffee.txt"), "кава зранку з молоком\n\nкава бадьорить");
        File.WriteAllText(Path.Combine(_notes, "empty.txt"), string.Empty);
        File.WriteAllText(Path.Combine(_notes, "image.png"), "не підтримується");

        var trip = Path.Combine(_notes, "поїздки");
        Directory.CreateDirectory(trip);
        File.WriteAllText(
            Path.Combine(trip, "подорож.md"),
            "# Подорож\n\nкупити квитки на потяг до Львова\n\n- взяти парасолю");

        CopyFixture("sample.docx", "plan.docx");
        CopyFixture("sample.pdf", "report.pdf");
        CopyFixture("scanned.pdf", "scan.pdf");
        CopyFixture("corrupted.pdf", "broken.pdf");
        CopyFixture("corrupted.docx", "broken.docx");
    }

    private void CopyFixture(string fixture, string name) =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", fixture), Path.Combine(_notes, name));

    private async Task<TestHost> StartAsync()
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

        // Фейки реєструються першими: AddLocalMind* використовують TryAdd і їх не перезапишуть.
        var provider = new ServiceCollection()
            .AddSingleton<IEmbedder>(embedder)
            .AddSingleton<ITokenCounter, FakeTokenCounter>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddLocalMindCore(configuration)
            .AddLocalMindSearch(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        await provider.InitializeLocalMindStorageAsync();
        return new TestHost(provider, embedder);
    }

    private static async Task WaitCompletedAsync(IIndexingService indexing)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (indexing.Progress is not { IsCompleted: true, Total: > 0 })
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Індексація не завершилась: {indexing.Progress}.");
            }

            await Task.Delay(50);
        }
    }

    private static async Task<string> TopFileAsync(TestHost host, string text)
    {
        var results = await host.Search.SearchAsync(new SearchQuery(text, SearchMode.Semantic));
        Assert.NotEmpty(results);
        return Path.GetFileName(results[0].DocumentPath);
    }

    private sealed class TestHost(ServiceProvider provider, FakeEmbedder embedder) : IAsyncDisposable
    {
        public FakeEmbedder Embedder { get; } = embedder;

        public IIndexingService Indexing { get; } = provider.GetRequiredService<IIndexingService>();

        public ISearchService Search { get; } = provider.GetRequiredService<ISearchService>();

        public IDocumentRepository Repository { get; } = provider.GetRequiredService<IDocumentRepository>();

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
