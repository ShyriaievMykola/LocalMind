using LocalMind.Search.Embeddings;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Tests.Embeddings;

public class ModelTokenizerTests
{
    private static ModelTokenizer CreateTokenizer() => new(Options.Create(TestModel.Options()));

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
        Assert.Equal(0, new HfTokenCounter(CreateTokenizer()).CountTokens(string.Empty));
    }

    [ModelFact]
    public void CountTokens_EqualsLengthOfEncodedText()
    {
        var tokenizer = CreateTokenizer();
        const string Text = "Привіт, світ! Hello world.";

        Assert.Equal(tokenizer.Encode(Text, 512).Length, new HfTokenCounter(tokenizer).CountTokens(Text));
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
    public void Encode_EmptyText_HasOnlySpecialTokens()
    {
        Assert.Equal([0L, 2L], CreateTokenizer().Encode(string.Empty, 16));
    }
}
