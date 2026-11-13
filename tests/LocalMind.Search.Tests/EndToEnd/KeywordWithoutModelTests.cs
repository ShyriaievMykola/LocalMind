using LocalMind.Core;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Models;
using LocalMind.Core.Storage;
using LocalMind.Search.Configuration;
using LocalMind.Search.Embeddings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Tests.EndToEnd;

/// <summary>
/// Справжні ModelTokenizer, HfTokenCounter і OnnxEmbedder, але без файлів моделі: індексація й keyword-пошук
/// мають працювати, а semantic — давати зрозумілу помилку.
/// </summary>
public sealed class KeywordWithoutModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("localmind-nomodel-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Index_WithoutModelFiles_StoresChunksWithoutVectors()
    {
        await using var provider = await StartAsync();

        var result = await IndexLibraryAsync(provider);

        Assert.Equal(2, result.Indexed);
        Assert.Equal(2, result.WithoutVectors);
        Assert.Equal(0, result.Failed);
        Assert.True(result.Chunks >= 2);
        var repository = provider.GetRequiredService<IDocumentRepository>();
        Assert.All(await repository.GetDocumentsAsync(), d => Assert.Equal(DocumentStatus.Indexed, d.Status));
        Assert.Empty(await repository.GetVectorsAsync(provider.GetRequiredService<IEmbedder>().ModelName).ToListAsync());
    }

    [Fact]
    public async Task Search_Keyword_FindsDocumentsIndexedWithoutModel()
    {
        await using var provider = await StartAsync();
        await IndexLibraryAsync(provider);

        var results = await provider.GetRequiredService<ISearchService>()
            .SearchAsync(new SearchQuery("кава зранку", SearchMode.Keyword));

        Assert.Equal("coffee.txt", Path.GetFileName(results[0].DocumentPath));
    }

    [Fact]
    public async Task Search_Semantic_ExplainsThatModelIsMissing()
    {
        await using var provider = await StartAsync();
        await IndexLibraryAsync(provider);

        var exception = await Assert.ThrowsAsync<ModelNotFoundException>(
            () => provider.GetRequiredService<ISearchService>().SearchAsync(new SearchQuery("кава", SearchMode.Semantic)));

        Assert.Contains("scripts/download-models", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_Hybrid_FallsBackToKeywordResults()
    {
        await using var provider = await StartAsync();
        await IndexLibraryAsync(provider);

        var results = await provider.GetRequiredService<ISearchService>()
            .SearchAsync(new SearchQuery("кава зранку", SearchMode.Hybrid));

        Assert.Equal("coffee.txt", Path.GetFileName(results[0].DocumentPath));
    }

    [Fact]
    public void TokenCounter_WithoutTokenizerFile_EstimatesByLength()
    {
        var options = Options.Create(new EmbeddingModelOptions
        {
            ModelName = "m",
            ModelPath = Path.Combine(_root, "немає.onnx"),
            TokenizerPath = Path.Combine(_root, "немає.model"),
            Dimensions = 8,
            PassagePrefix = "passage: ",
        });
        var counter = new HfTokenCounter(new ModelTokenizer(options), options);

        // Префікс (9 символів) + текст (30) = 39 символів → 13 токенів + 2 службових.
        Assert.Equal(15, counter.CountTokens(new string('я', 30)));
        Assert.Equal(0, counter.CountTokens(string.Empty));
    }

    [Fact]
    public async Task Embedder_WithoutModelFile_ThrowsModelNotFound()
    {
        var options = Options.Create(new EmbeddingModelOptions
        {
            ModelName = "m",
            ModelPath = Path.Combine(_root, "немає.onnx"),
            TokenizerPath = Path.Combine(_root, "немає.model"),
            Dimensions = 8,
        });
        using var embedder = new OnnxEmbedder(options, new ModelTokenizer(options));

        await Assert.ThrowsAsync<ModelNotFoundException>(() => embedder.EmbedAsync(["текст"], EmbeddingPurpose.Query));
    }

    private async Task<FolderIndexingResult> IndexLibraryAsync(ServiceProvider provider)
    {
        var notes = Path.Combine(_root, "нотатки");
        Directory.CreateDirectory(notes);
        File.WriteAllText(Path.Combine(notes, "coffee.txt"), "кава зранку з молоком\n\nкава бадьорить");
        File.WriteAllText(Path.Combine(notes, "tea.md"), "# Чай\n\nзелений чай увечері");

        return await provider.GetRequiredService<FolderIndexer>().IndexAsync(notes);
    }

    private async Task<ServiceProvider> StartAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{StorageOptions.SectionName}:{nameof(StorageOptions.DatabasePath)}"] = Path.Combine(_root, "db", "localmind.db"),
                [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelName)}"] = "absent-model",
                [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelPath)}"] = Path.Combine(_root, "немає.onnx"),
                [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.TokenizerPath)}"] = Path.Combine(_root, "немає.model"),
                [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.Dimensions)}"] = "8",
            })
            .Build();

        var provider = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddLocalMindCore(configuration)
            .AddLocalMindSearch(configuration)
            .BuildServiceProvider();
        await provider.InitializeLocalMindStorageAsync();
        return provider;
    }
}
