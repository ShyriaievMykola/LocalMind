using System.Globalization;
using LocalMind.Core;
using LocalMind.Core.Configuration;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Tests.Configuration;

public class SearchConfigurationTests
{
    private const string Embedding = EmbeddingModelOptions.SectionName;
    private const string Search = SearchOptions.SectionName;
    private const string Chunking = ChunkingOptions.SectionName;

    /// <summary>Мінімальна коректна конфігурація моделі; окремі ключі тести перевизначають.</summary>
    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        [$"{Embedding}:ModelName"] = "multilingual-e5-small",
        [$"{Embedding}:ModelPath"] = "models/multilingual-e5-small/model.onnx",
        [$"{Embedding}:TokenizerPath"] = "models/multilingual-e5-small/sentencepiece.bpe.model",
        [$"{Embedding}:Dimensions"] = "384",
    };

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        return new ServiceCollection()
            .AddLocalMindCore(configuration)
            .AddLocalMindSearch(configuration)
            .BuildServiceProvider();
    }

    private static ServiceProvider BuildProvider(string key, string? value)
    {
        var values = ValidConfiguration();
        values[key] = value;
        return BuildProvider(values);
    }

    [Fact]
    public void ValidConfiguration_PassesStartupValidationWithDefaults()
    {
        using var provider = BuildProvider(ValidConfiguration());

        provider.GetRequiredService<IStartupValidator>().Validate();

        var search = provider.GetRequiredService<IOptions<SearchOptions>>().Value;
        var embedding = provider.GetRequiredService<IOptions<EmbeddingModelOptions>>().Value;
        Assert.Equal(10, search.DefaultTopK);
        Assert.Equal(50, search.CandidatesPerIndex);
        Assert.Equal(60, search.RrfK);
        Assert.Equal("multilingual-e5-small", embedding.ModelName);
        Assert.Equal(384, embedding.Dimensions);
        Assert.Equal(512, embedding.MaxTokens);
        Assert.Equal(32, embedding.BatchSize);
        Assert.Equal(string.Empty, embedding.QueryPrefix);
    }

    [Fact]
    public void Prefixes_AreBound()
    {
        var values = ValidConfiguration();
        values[$"{Embedding}:QueryPrefix"] = "query: ";
        values[$"{Embedding}:PassagePrefix"] = "passage: ";
        using var provider = BuildProvider(values);

        var embedding = provider.GetRequiredService<IOptions<EmbeddingModelOptions>>().Value;

        Assert.Equal("query: ", embedding.QueryPrefix);
        Assert.Equal("passage: ", embedding.PassagePrefix);
    }

    [Theory]
    [InlineData("DefaultTopK", "0")]
    [InlineData("CandidatesPerIndex", "5")]
    [InlineData("RrfK", "0")]
    public void InvalidSearchOptions_FailValidation(string key, string value)
    {
        using var provider = BuildProvider($"{Search}:{key}", value);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SearchOptions>>().Value);

        Assert.Contains($"{Search}:{key}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ModelName", "")]
    [InlineData("ModelPath", " ")]
    [InlineData("TokenizerPath", "")]
    [InlineData("Dimensions", "0")]
    [InlineData("MaxTokens", "0")]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "1025")]
    public void InvalidEmbeddingOptions_FailValidation(string key, string value)
    {
        using var provider = BuildProvider($"{Embedding}:{key}", value);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<EmbeddingModelOptions>>().Value);

        Assert.Contains($"{Embedding}:{key}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingEmbeddingSection_FailsStartupValidationOnce()
    {
        using var provider = BuildProvider([]);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Equal(typeof(EmbeddingModelOptions), exception.OptionsType);
    }

    [Fact]
    public void InvalidEmbeddingOptions_SkipChunkSizeCheck()
    {
        var validator = new ChunkSizeWithinModelLimitValidator(new InvalidEmbeddingOptions());

        Assert.True(validator.Validate(null, new ChunkingOptions()).Skipped);
    }

    private sealed class InvalidEmbeddingOptions : IOptions<EmbeddingModelOptions>
    {
        public EmbeddingModelOptions Value =>
            throw new OptionsValidationException(string.Empty, typeof(EmbeddingModelOptions), ["помилка"]);
    }

    [Fact]
    public void BatchSizeAtMaximum_IsValid()
    {
        using var provider = BuildProvider($"{Embedding}:BatchSize", EmbeddingModelOptions.MaxBatchSize.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(
            EmbeddingModelOptions.MaxBatchSize,
            provider.GetRequiredService<IOptions<EmbeddingModelOptions>>().Value.BatchSize);
    }

    [Fact]
    public void ChunkSizeAboveModelLimit_FailsChunkingValidation()
    {
        var values = ValidConfiguration();
        values[$"{Embedding}:MaxTokens"] = "512";
        values[$"{Chunking}:ChunkSizeTokens"] = "600";
        using var provider = BuildProvider(values);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ChunkingOptions>>().Value);

        Assert.Contains("MaxTokens", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChunkSizeEqualToModelLimit_IsValid()
    {
        var values = ValidConfiguration();
        values[$"{Embedding}:MaxTokens"] = "512";
        values[$"{Chunking}:ChunkSizeTokens"] = "512";
        using var provider = BuildProvider(values);

        Assert.Equal(512, provider.GetRequiredService<IOptions<ChunkingOptions>>().Value.ChunkSizeTokens);
    }

    [Fact]
    public void EmbeddingValidator_NullPrefix_Fails()
    {
        var options = new EmbeddingModelOptions
        {
            ModelName = "m",
            ModelPath = "m.onnx",
            TokenizerPath = "t.json",
            Dimensions = 8,
            QueryPrefix = null!,
        };

        var result = new EmbeddingModelOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("QueryPrefix", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validators_NullOptions_Throw()
    {
        var limitValidator = new ChunkSizeWithinModelLimitValidator(Options.Create(new EmbeddingModelOptions()));

        Assert.Throws<ArgumentNullException>(() => new SearchOptionsValidator().Validate(null, null!));
        Assert.Throws<ArgumentNullException>(() => new EmbeddingModelOptionsValidator().Validate(null, null!));
        Assert.Throws<ArgumentNullException>(() => limitValidator.Validate(null, null!));
    }

    [Fact]
    public void AddLocalMindSearch_NullArguments_Throw()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddLocalMindSearch(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddLocalMindSearch(null!));
    }
}
