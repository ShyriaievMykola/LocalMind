using LocalMind.Core.Configuration;
using LocalMind.Core.Ingestion.Chunking;
using LocalMind.Search.Embeddings;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Tests.Embeddings;

public class ModelTokenizerTests
{
    private static ModelTokenizer CreateTokenizer() => new(Options.Create(TestModel.Options()));

    private static HfTokenCounter CreateCounter() => new(CreateTokenizer(), Options.Create(TestModel.Options()));

    /// <summary>Очікувані номери отримано еталонним токенізатором Hugging Face (<c>tokenizer.json</c>).</summary>
    [ModelFact]
    public void Encode_MatchesReferenceTokenizer()
    {
        var tokenizer = CreateTokenizer();

        Assert.Equal([0L, 35378L, 8999L, 2L], tokenizer.Encode("Hello world", 512));
        Assert.Equal(
            [0L, 46692L, 12L, 1813L, 24046L, 4L, 31367L, 38L, 4910L, 43282L, 32L, 2L],
            tokenizer.Encode("passage: Привіт, світ! Як справи?", 512));
    }

    [ModelFact]
    public void CountTokens_EmptyText_IsZero()
    {
        Assert.Equal(0, CreateCounter().CountTokens(string.Empty));
    }

    [ModelFact]
    public void CountTokens_IncludesPassagePrefixAndSpecialTokens()
    {
        const string Text = "Привіт, світ! Hello world.";
        var options = TestModel.Options();

        Assert.Equal(
            CreateTokenizer().Encode(options.PassagePrefix + Text, 512).Length,
            CreateCounter().CountTokens(Text));
    }

    [ModelFact]
    public void Chunks_FitModelLimitWithPrefix_SoNothingIsTruncated()
    {
        const int Limit = 32;
        var options = TestModel.Options();
        var chunker = new FixedSizeChunker(
            Options.Create(new ChunkingOptions { ChunkSizeTokens = Limit }),
            CreateCounter());
        var text = string.Join(' ', Enumerable.Repeat("Кава зранку допомагає прокинутися, а чай увечері заспокоює.", 50));

        var tokenizer = CreateTokenizer();

        var chunks = chunker.Split(Guid.NewGuid(), text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk =>
            Assert.True(tokenizer.Encode(options.PassagePrefix + chunk.Text, 512).Length <= Limit));
    }

    [ModelFact]
    public void Chunks_WordLongerThanLimit_FitModelLimitWithPrefixAndKeepAllText()
    {
        const int Limit = 32;
        var options = TestModel.Options();
        var chunker = new FixedSizeChunker(
            Options.Create(new ChunkingOptions { ChunkSizeTokens = Limit, OverlapPercent = 0 }),
            CreateCounter());
        var word = string.Concat(Enumerable.Repeat("aGVsbG8gd29ybGQ", 30)); // base64 без пробілів
        var text = $"початок {word} кінець";

        var tokenizer = CreateTokenizer();

        var chunks = chunker.Split(Guid.NewGuid(), text);

        Assert.True(chunks.Count > 2);
        Assert.All(chunks, chunk =>
            Assert.True(tokenizer.Encode(options.PassagePrefix + chunk.Text, 512).Length <= Limit));
        Assert.Equal(text.Replace(" ", string.Empty, StringComparison.Ordinal), string.Concat(chunks.Select(c => c.Text.Replace(" ", string.Empty, StringComparison.Ordinal))));
    }

    [ModelFact]
    public void Encode_LongText_IsTruncatedAndKeepsEndToken()
    {
        var ids = CreateTokenizer().Encode(string.Concat(Enumerable.Repeat("слово ", 1000)), 16);

        Assert.Equal(16, ids.Length);
        Assert.Equal(0, ids[0]);
        Assert.Equal(2, ids[^1]);
    }

    [ModelFact]
    public void Encode_SmallestLimit_KeepsOneTextToken()
    {
        var ids = CreateTokenizer().Encode("слово слово слово", 3);

        Assert.Equal(3, ids.Length);
        Assert.Equal(2, ids[^1]);
    }

    [ModelFact]
    public void Encode_EmptyText_HasOnlySpecialTokens()
    {
        Assert.Equal([0L, 2L], CreateTokenizer().Encode(string.Empty, 16));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Encode_LimitTooSmall_Throws(int maxTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateTokenizer().Encode("текст", maxTokens));
    }
}
