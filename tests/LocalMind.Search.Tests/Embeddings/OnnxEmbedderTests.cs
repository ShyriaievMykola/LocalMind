using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using LocalMind.Search.Embeddings;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Tests.Embeddings;

public class OnnxEmbedderTests
{
    [ModelFact]
    public async Task EmbedAsync_ReturnsNormalizedVectorsOfModelDimensions()
    {
        using var embedder = CreateEmbedder();

        var vectors = await embedder.EmbedAsync(["Привіт, світ!", "Hello world"], EmbeddingPurpose.Passage);

        Assert.Equal(2, vectors.Count);
        Assert.All(vectors, vector =>
        {
            Assert.Equal(384, vector.Length);
            Assert.Equal(1f, Dot(vector.Span, vector.Span), 0.001f);
        });
    }

    [ModelFact]
    public async Task EmbedAsync_SimilarTextsAreCloserThanDifferent()
    {
        using var embedder = CreateEmbedder();

        var similarity = await SimilaritiesAsync(
            embedder,
            "Як приготувати борщ?",
            "Рецепт українського борщу з буряком і капустою",
            "Транзакції в базі даних гарантують атомарність");

        Assert.True(similarity.Related > similarity.Unrelated, $"{similarity.Related} <= {similarity.Unrelated}");
    }

    [ModelFact]
    public async Task EmbedAsync_FindsMeaningAcrossLanguages()
    {
        using var embedder = CreateEmbedder();

        var similarity = await SimilaritiesAsync(
            embedder,
            "how to cook soup",
            "Рецепт українського борщу з буряком і капустою",
            "Транзакції в базі даних гарантують атомарність");

        Assert.True(similarity.Related > similarity.Unrelated, $"{similarity.Related} <= {similarity.Unrelated}");
    }

    [ModelFact]
    public async Task EmbedAsync_BatchingKeepsOrderAndValues()
    {
        string[] texts = ["коротко", "трохи довший текст про каву і чай", "середній текст", "ще один"];
        using var batched = CreateEmbedder(batchSize: 2);
        using var single = CreateEmbedder(batchSize: 1);

        var fromBatches = await batched.EmbedAsync(texts, EmbeddingPurpose.Passage);
        var oneByOne = await single.EmbedAsync(texts, EmbeddingPurpose.Passage);

        for (var i = 0; i < texts.Length; i++)
        {
            Assert.True(Dot(fromBatches[i].Span, oneByOne[i].Span) > 0.999f);
        }
    }

    [ModelFact]
    public async Task EmbedAsync_PurposeAddsDifferentPrefix()
    {
        using var embedder = CreateEmbedder();

        var query = await embedder.EmbedAsync(["кава"], EmbeddingPurpose.Query);
        var passage = await embedder.EmbedAsync(["кава"], EmbeddingPurpose.Passage);

        Assert.False(query[0].Span.SequenceEqual(passage[0].Span));
    }

    [ModelFact]
    public async Task EmbedAsync_TextLongerThanLimit_IsTruncatedWithoutException()
    {
        using var embedder = CreateEmbedder();
        var longText = string.Concat(Enumerable.Repeat("дуже довгий текст ", 2000));

        var vectors = await embedder.EmbedAsync([longText, string.Empty], EmbeddingPurpose.Passage);

        Assert.Equal(384, vectors[0].Length);
        Assert.Equal(384, vectors[1].Length);
    }

    [Fact]
    public async Task EmbedAsync_EmptySet_ReturnsEmptyWithoutLoadingModel()
    {
        using var embedder = CreateEmbedderWithMissingFiles();

        Assert.Empty(await embedder.EmbedAsync([], EmbeddingPurpose.Query));
    }

    [Fact]
    public async Task EmbedAsync_NullText_Throws()
    {
        using var embedder = CreateEmbedderWithMissingFiles();

        await Assert.ThrowsAsync<ArgumentException>(() => embedder.EmbedAsync([null!], EmbeddingPurpose.Query));
    }

    [Fact]
    public async Task EmbedAsync_Canceled_Throws()
    {
        using var embedder = CreateEmbedderWithMissingFiles();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => embedder.EmbedAsync(["текст"], EmbeddingPurpose.Query, cancellation.Token));
    }

    [Fact]
    public async Task EmbedAsync_MissingTokenizerFile_ExplainsWhatToDo()
    {
        using var embedder = CreateEmbedderWithMissingFiles();

        var exception = await Assert.ThrowsAsync<LocalMindException>(
            () => embedder.EmbedAsync(["текст"], EmbeddingPurpose.Query));

        Assert.Contains("download-models", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedAsync_DamagedTokenizerFile_ThrowsLocalMindException()
    {
        using var folder = new TempFolder();
        var options = Options.Create(new EmbeddingModelOptions
        {
            ModelName = "damaged",
            ModelPath = folder.Write("model.onnx", "не модель"),
            TokenizerPath = folder.Write("tokenizer.model", "не токенізатор"),
            Dimensions = 4,
        });
        using var embedder = new OnnxEmbedder(options, new ModelTokenizer(options));

        var exception = await Assert.ThrowsAsync<LocalMindException>(
            () => embedder.EmbedAsync(["текст"], EmbeddingPurpose.Query));

        Assert.Contains("пошкоджений", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }

    [ModelFact]
    public async Task EmbedAsync_DamagedModelFile_ThrowsLocalMindException()
    {
        using var folder = new TempFolder();
        var options = TestModel.Options();
        options.ModelPath = folder.Write("model.onnx", "обірване завантаження");
        var wrapped = Options.Create(options);
        using var embedder = new OnnxEmbedder(wrapped, new ModelTokenizer(wrapped));

        var exception = await Assert.ThrowsAsync<LocalMindException>(
            () => embedder.EmbedAsync(["текст"], EmbeddingPurpose.Query));

        Assert.IsType<Microsoft.ML.OnnxRuntime.OnnxRuntimeException>(exception.InnerException);
    }

    [ModelFact]
    public async Task EmbedAsync_FileAppearsLater_LoadsWithoutRestart()
    {
        // Токенізатор, а не модель: він малий, а кешування однакове.
        using var folder = new TempFolder();
        var options = TestModel.Options();
        var source = options.TokenizerPath;
        options.TokenizerPath = Path.Combine(folder.Path, "sentencepiece.bpe.model");
        var wrapped = Options.Create(options);
        using var embedder = new OnnxEmbedder(wrapped, new ModelTokenizer(wrapped));
        await Assert.ThrowsAsync<LocalMindException>(() => embedder.EmbedAsync(["текст"], EmbeddingPurpose.Query));

        File.Copy(source, options.TokenizerPath);

        Assert.Single(await embedder.EmbedAsync(["текст"], EmbeddingPurpose.Query));
    }

    private static async Task<(float Related, float Unrelated)> SimilaritiesAsync(
        OnnxEmbedder embedder,
        string query,
        string related,
        string unrelated)
    {
        var queryVector = (await embedder.EmbedAsync([query], EmbeddingPurpose.Query))[0];
        var passages = await embedder.EmbedAsync([related, unrelated], EmbeddingPurpose.Passage);

        return (
            Dot(queryVector.Span, passages[0].Span),
            Dot(queryVector.Span, passages[1].Span));
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

    private static OnnxEmbedder CreateEmbedder(int batchSize = 32)
    {
        var options = Options.Create(TestModel.Options(batchSize));
        return new OnnxEmbedder(options, new ModelTokenizer(options));
    }

    private static OnnxEmbedder CreateEmbedderWithMissingFiles()
    {
        var options = Options.Create(new EmbeddingModelOptions
        {
            ModelName = "missing",
            ModelPath = "missing/model.onnx",
            TokenizerPath = "missing/tokenizer.model",
            Dimensions = 4,
        });
        return new OnnxEmbedder(options, new ModelTokenizer(options));
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("localmind-").FullName;

        public string Write(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
