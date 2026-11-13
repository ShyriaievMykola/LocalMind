using System.Text.Json;
using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using LocalMind.Search.Embeddings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Tests.Configuration;

public class ModelCatalogTests
{
    private static readonly string[] ExpectedModels =
    [
        "multilingual-e5-small",
        "multilingual-e5-base",
        "paraphrase-multilingual-MiniLM-L12-v2",
        "paraphrase-multilingual-mpnet-base-v2",
    ];

    public static TheoryData<string> Hosts { get; } = new() { "App", "Cli" };

    public static TheoryData<string> Models { get; } = new(ExpectedModels);

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Catalog_InAppSettings_IsValidAndHasTheAgreedModels(string host)
    {
        var catalog = LoadCatalog(host);

        Assert.Equal(ExpectedModels, catalog.Models.Select(m => m.Name));
        Assert.True(new ModelCatalogOptionsValidator().Validate(null, catalog).Succeeded);
    }

    [Fact]
    public void Catalog_AppAndCli_AreIdentical()
    {
        Assert.Equal(
            JsonSerializer.Serialize(LoadCatalog("App")),
            JsonSerializer.Serialize(LoadCatalog("Cli")));
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void DownloadScripts_ContainEveryCatalogModelWithSameRevisionAndHashes(string name)
    {
        var model = LoadCatalog("App").Models.Single(m => m.Name == name);

        foreach (var script in (string[])["download-models.ps1", "download-models.sh"])
        {
            var text = File.ReadAllText(RepositoryFile("scripts", script));
            Assert.Contains(model.Name, text, StringComparison.Ordinal);
            Assert.Contains(model.Repository, text, StringComparison.Ordinal);
            Assert.Contains(model.Revision, text, StringComparison.Ordinal);
            Assert.Contains(model.ModelSha256, text, StringComparison.Ordinal);
            Assert.Contains(model.TokenizerSha256, text, StringComparison.Ordinal);
            Assert.Contains(model.ModelFile, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void AppSettings_SelectedModel_IsInTheCatalog(string host)
    {
        var configuration = LoadConfiguration(host);
        var selected = configuration[$"{EmbeddingModelOptions.SectionName}:ModelName"];

        Assert.Contains(selected, LoadCatalog(host).Models.Select(m => m.Name));
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void Embedding_SelectedByNameOnly_TakesEverythingFromTheCatalog(string name)
    {
        var expected = LoadCatalog("App").Models.Single(m => m.Name == name);

        var options = ResolveEmbedding(LoadConfiguration("App"), name);

        Assert.Equal(name, options.ModelName);
        Assert.Equal($"models/{name}/model.onnx", options.ModelPath);
        Assert.Equal($"models/{name}/sentencepiece.bpe.model", options.TokenizerPath);
        Assert.Equal(expected.ModelSha256, options.ModelSha256);
        Assert.Equal(expected.TokenizerSha256, options.TokenizerSha256);
        Assert.Equal(expected.Dimensions, options.Dimensions);
        Assert.Equal(expected.MaxTokens, options.MaxTokens);
        Assert.Equal(expected.QueryPrefix, options.QueryPrefix);
        Assert.Equal(expected.PassagePrefix, options.PassagePrefix);
        Assert.Equal(16, options.BatchSize);
        Assert.True(new EmbeddingModelOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Embedding_CatalogModelWithCustomPath_KeepsPathButTakesTheRestFromCatalog()
    {
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(LoadConfiguration("App"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{EmbeddingModelOptions.SectionName}:ModelName"] = "multilingual-e5-small",
                [$"{EmbeddingModelOptions.SectionName}:ModelPath"] = "custom/model.onnx",
            })
            .Build();

        var options = ResolveEmbedding(configuration, "multilingual-e5-small");

        Assert.Equal("custom/model.onnx", options.ModelPath);
        Assert.Equal("models/multilingual-e5-small/sentencepiece.bpe.model", options.TokenizerPath);
        Assert.Equal(384, options.Dimensions);
        Assert.Equal("query: ", options.QueryPrefix);
    }

    [Theory]
    [InlineData("models/multilingual-e5-small/model.onnx", "models/multilingual-e5-small/sentencepiece.bpe.model")]
    [InlineData("models\\multilingual-e5-small\\model.onnx", "models\\multilingual-e5-small\\sentencepiece.bpe.model")]
    [InlineData("MODELS/Multilingual-E5-Small/model.onnx", "MODELS/Multilingual-E5-Small/sentencepiece.bpe.model")]
    [InlineData("C:/app/models/multilingual-e5-small/model.onnx", "C:/app/models/multilingual-e5-small/sentencepiece.bpe.model")]
    public void Embedding_PathLeftFromAnotherCatalogModel_IsReplacedByTheSelectedModelsPath(string modelPath, string tokenizerPath)
    {
        // Застосунок зберігає в settings.json шлях попередньої моделі; нова модель не має вказувати на чужий файл.
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(LoadConfiguration("App"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{EmbeddingModelOptions.SectionName}:ModelPath"] = modelPath,
                [$"{EmbeddingModelOptions.SectionName}:TokenizerPath"] = tokenizerPath,
            })
            .Build();

        var options = ResolveEmbedding(configuration, "multilingual-e5-base");

        Assert.Equal("models/multilingual-e5-base/model.onnx", options.ModelPath);
        Assert.Equal("models/multilingual-e5-base/sentencepiece.bpe.model", options.TokenizerPath);
        Assert.Equal(768, options.Dimensions);
    }

    [Fact]
    public void Embedding_OwnDefaultPathOfTheSelectedModel_IsKept()
    {
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(LoadConfiguration("App"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{EmbeddingModelOptions.SectionName}:ModelPath"] = "models/multilingual-e5-base/model.onnx",
            })
            .Build();

        var options = ResolveEmbedding(configuration, "multilingual-e5-base");

        Assert.Equal("models/multilingual-e5-base/model.onnx", options.ModelPath);
    }

    [Fact]
    public void Embedding_ModelNotInCatalog_IsLeftAsConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(LoadConfiguration("App"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{EmbeddingModelOptions.SectionName}:ModelName"] = "my-own-model",
                [$"{EmbeddingModelOptions.SectionName}:ModelPath"] = "custom/model.onnx",
                [$"{EmbeddingModelOptions.SectionName}:TokenizerPath"] = "custom/tokenizer.model",
                [$"{EmbeddingModelOptions.SectionName}:Dimensions"] = "7",
            })
            .Build();

        var options = ResolveEmbedding(configuration, "my-own-model");

        Assert.Equal("custom/model.onnx", options.ModelPath);
        Assert.Equal(7, options.Dimensions);
        Assert.Equal(string.Empty, options.ModelSha256);
    }

    [Fact]
    public void Embedding_UnknownModelName_FailsValidationWithMissingPath()
    {
        var exception = Assert.Throws<OptionsValidationException>(
            () => ResolveEmbedding(LoadConfiguration("App"), "no-such-model"));

        Assert.Contains("ModelPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParaphraseModels_DefaultChunkSize_ExceedsTheirLimitAndIsReported()
    {
        var options = ResolveEmbedding(LoadConfiguration("App"), "paraphrase-multilingual-MiniLM-L12-v2");
        var validator = new ChunkSizeWithinModelLimitValidator(Options.Create(options));

        var result = validator.Validate(null, new ChunkingOptions());

        Assert.True(result.Failed);
        Assert.Contains("MaxTokens", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Зменште LocalMind:Chunking:ChunkSizeTokens до ~120", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ParaphraseModels_ChunkSizeWithinTheirLimit_Passes()
    {
        var options = ResolveEmbedding(LoadConfiguration("App"), "paraphrase-multilingual-MiniLM-L12-v2");
        var validator = new ChunkSizeWithinModelLimitValidator(Options.Create(options));

        Assert.True(validator.Validate(null, new ChunkingOptions { ChunkSizeTokens = 120 }).Succeeded);
    }

    [Fact]
    public void Validator_BrokenModels_ReportsEachProblem()
    {
        var catalog = new ModelCatalogOptions
        {
            Models =
            [
                new CatalogModel { Name = "../escape", Repository = "a/b", License = "MIT", Revision = "abc", ModelSha256 = "00", TokenizerSha256 = new string('a', 64), Dimensions = 0, ModelBytes = 1 },
                new CatalogModel { Name = "dup", Repository = "a/b", License = "MIT", Revision = new string('a', 40), ModelSha256 = new string('b', 64), TokenizerSha256 = new string('c', 64), Dimensions = 4, ModelBytes = 1 },
                new CatalogModel { Name = "DUP", Repository = "a/b", License = "MIT", Revision = new string('a', 40), ModelSha256 = new string('b', 64), TokenizerSha256 = new string('c', 64), Dimensions = 4, ModelBytes = 1 },
            ],
        };

        var result = new ModelCatalogOptionsValidator().Validate(null, catalog);

        Assert.True(result.Failed);
        var message = result.FailureMessage;
        Assert.Contains("Models:0:Name", message, StringComparison.Ordinal);
        Assert.Contains("Models:0:Revision", message, StringComparison.Ordinal);
        Assert.Contains("Models:0:ModelSha256", message, StringComparison.Ordinal);
        Assert.Contains("Models:0:Dimensions", message, StringComparison.Ordinal);
        Assert.Contains("повторюється", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("na:me")]
    [InlineData("na*me")]
    public void Validator_NameIsNotAPlainFolderName_Fails(string name)
    {
        var catalog = new ModelCatalogOptions { Models = [ValidModel(name: name)] };

        var result = new ModelCatalogOptionsValidator().Validate(null, catalog);

        Assert.True(result.Failed);
        Assert.Contains("Models:0:Name", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../model.onnx")]
    [InlineData("onnx/../../model.onnx")]
    [InlineData("/etc/model.onnx")]
    [InlineData("C:\\model.onnx")]
    [InlineData("onnx\\model.onnx")]
    [InlineData("onnx//model.onnx")]
    [InlineData("onnx/mo*del.onnx")]
    public void Validator_FilePathEscapesTheRepository_Fails(string file)
    {
        var withModelFile = new ModelCatalogOptions { Models = [ValidModel(modelFile: file)] };
        var withTokenizerFile = new ModelCatalogOptions { Models = [ValidModel(tokenizerFile: file)] };

        Assert.Contains("ModelFile", new ModelCatalogOptionsValidator().Validate(null, withModelFile).FailureMessage, StringComparison.Ordinal);
        Assert.Contains("TokenizerFile", new ModelCatalogOptionsValidator().Validate(null, withTokenizerFile).FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_NestedRelativeFilePath_Passes()
    {
        var catalog = new ModelCatalogOptions { Models = [ValidModel(modelFile: "onnx/model.onnx")] };

        Assert.True(new ModelCatalogOptionsValidator().Validate(null, catalog).Succeeded);
    }

    private static CatalogModel ValidModel(string name = "ok", string modelFile = "onnx/model.onnx", string tokenizerFile = "sentencepiece.bpe.model") =>
        new()
        {
            Name = name,
            Repository = "a/b",
            License = "MIT",
            Revision = new string('a', 40),
            ModelFile = modelFile,
            TokenizerFile = tokenizerFile,
            ModelSha256 = new string('b', 64),
            TokenizerSha256 = new string('c', 64),
            Dimensions = 4,
            ModelBytes = 1,
        };

    [CatalogModelTheory]
    [MemberData(nameof(Models))]
    public async Task DownloadedModel_RunsThroughOnnxEmbedder(string name)
    {
        var folder = CatalogModelTheoryAttribute.FolderOf(name);
        if (folder is null)
        {
            return; // Цю модель не завантажено: scripts/download-models <назва>.
        }

        var model = LoadCatalog("App").Models.Single(m => m.Name == name);
        var options = Options.Create(new EmbeddingModelOptions
        {
            ModelName = name,
            ModelPath = Path.Combine(folder, "model.onnx"),
            TokenizerPath = Path.Combine(folder, "sentencepiece.bpe.model"),
            ModelSha256 = model.ModelSha256,
            TokenizerSha256 = model.TokenizerSha256,
            Dimensions = model.Dimensions,
            MaxTokens = model.MaxTokens,
            QueryPrefix = model.QueryPrefix,
            PassagePrefix = model.PassagePrefix,
            BatchSize = 8,
        });
        using var embedder = new OnnxEmbedder(options, new ModelTokenizer(options));

        var query = (await embedder.EmbedAsync(["how to cook soup"], EmbeddingPurpose.Query))[0];
        var passages = await embedder.EmbedAsync(
            ["Рецепт українського борщу з буряком і капустою", "Транзакції в базі даних гарантують атомарність"],
            EmbeddingPurpose.Passage);

        Assert.Equal(model.Dimensions, query.Length);
        Assert.Equal(1f, Dot(query.Span, query.Span), 0.001f);
        Assert.True(Dot(query.Span, passages[0].Span) > Dot(query.Span, passages[1].Span));
    }

    private static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var sum = 0f;
        for (var i = 0; i < left.Length; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }

    private static EmbeddingModelOptions ResolveEmbedding(IConfiguration configuration, string modelName)
    {
        var overrides = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{EmbeddingModelOptions.SectionName}:ModelName"] = modelName,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLocalMindSearch(overrides);
        using var provider = services.BuildServiceProvider();

        // Без ValidateOnStart-перевірки хоста: потрібні самі значення, а помилки перевіряємо окремо.
        return provider.GetRequiredService<IOptionsFactory<EmbeddingModelOptions>>().Create(Options.DefaultName);
    }

    private static ModelCatalogOptions LoadCatalog(string host)
    {
        var catalog = new ModelCatalogOptions();
        LoadConfiguration(host).GetSection(ModelCatalogOptions.SectionName).Bind(catalog);
        return catalog;
    }

    private static IConfigurationRoot LoadConfiguration(string host) =>
        new ConfigurationBuilder()
            .AddJsonFile(RepositoryFile("src", $"LocalMind.{host}", "appsettings.json"), optional: false)
            .Build();

    internal static string RepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LocalMind.slnx")))
            {
                return Path.Combine([directory.FullName, .. parts]);
            }
        }

        throw new DirectoryNotFoundException("Корінь репозиторію (LocalMind.slnx) не знайдено над тестовою текою.");
    }
}

/// <summary>Теорія зі справжніми моделями каталогу; пропускається, якщо жодної не завантажено (наприклад, у CI).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CatalogModelTheoryAttribute : TheoryAttribute
{
    public CatalogModelTheoryAttribute()
    {
        if (!Directory.Exists(ModelsRoot()) || !Directory.EnumerateDirectories(ModelsRoot()).Any(HasModel))
        {
            Skip = "Моделі каталогу не завантажено: scripts/download-models all.";
        }
    }

    /// <summary>Тека завантаженої моделі або <see langword="null"/>.</summary>
    public static string? FolderOf(string name)
    {
        var folder = Path.Combine(ModelsRoot(), name);
        return HasModel(folder) ? folder : null;
    }

    private static bool HasModel(string folder) =>
        File.Exists(Path.Combine(folder, "model.onnx")) && File.Exists(Path.Combine(folder, "sentencepiece.bpe.model"));

    private static string ModelsRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "models");
            if (File.Exists(Path.Combine(directory.FullName, "LocalMind.slnx")))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
