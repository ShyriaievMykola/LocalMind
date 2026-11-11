using LocalMind.Core.Models;
using LocalMind.Core.Storage.FullText;
using LocalMind.Search.Snippets;

namespace LocalMind.Search.Tests.Snippets;

public class SnippetBuilderTests
{
    private static Chunk ChunkOf(string text) => new(Guid.NewGuid(), Guid.NewGuid(), 0, text, 0);

    private static string[] Highlighted(Snippet snippet) =>
        [.. snippet.Highlights.Select(h => snippet.Text.Substring(h.Start, h.Length))];

    [Fact]
    public void Build_ShortChunk_ReturnsWholeTextWithHighlights()
    {
        var snippet = SnippetBuilder.Build(ChunkOf("Кава зранку, чай увечері."), "кава");

        Assert.Equal("Кава зранку, чай увечері.", snippet.Text);
        Assert.Equal(["Кава"], Highlighted(snippet));
        Assert.False(snippet.HasTextBefore);
        Assert.False(snippet.HasTextAfter);
    }

    [Fact]
    public void Build_IgnoresCase_ForCyrillicAndLatin()
    {
        var snippet = SnippetBuilder.Build(ChunkOf("ДОКЕР і Docker Compose"), "докер docker");

        Assert.Equal(["ДОКЕР", "Docker"], Highlighted(snippet));
    }

    [Fact]
    public void Build_MatchesSimpleWordForms()
    {
        var snippet = SnippetBuilder.Build(
            ChunkOf("Випив каву. У нотатках про кави немає нічого."),
            "кава нотатки");

        Assert.Equal(["каву", "нотатках", "кави"], Highlighted(snippet));
    }

    [Fact]
    public void Build_SkipsStopWordsAndQuotes()
    {
        var snippet = SnippetBuilder.Build(ChunkOf("Кава і чай та м'ята на столі"), "\"і\" та на 'м'ята'");

        Assert.Equal(["м'ята"], Highlighted(snippet));
    }

    [Fact]
    public void Build_NoMatches_ReturnsStartOfChunkCutAtWordBoundary()
    {
        var text = string.Join(' ', Enumerable.Repeat("слово", 100));

        var snippet = SnippetBuilder.Build(ChunkOf(text), "машина", maxLength: 22);

        Assert.Equal("слово слово слово", snippet.Text);
        Assert.Empty(snippet.Highlights);
        Assert.Equal(0, snippet.Offset);
        Assert.False(snippet.HasTextBefore);
        Assert.True(snippet.HasTextAfter);
    }

    [Fact]
    public void Build_LongChunk_CentersOnMatchAndCutsAtWordBoundaries()
    {
        var filler = string.Join(' ', Enumerable.Repeat("текст", 40));
        var text = $"{filler} тут про каву і цукор {filler}";

        var snippet = SnippetBuilder.Build(ChunkOf(text), "кава цукор", maxLength: 60);

        Assert.True(snippet.Text.Length <= 60);
        Assert.Equal(["каву", "цукор"], Highlighted(snippet));
        Assert.True(snippet.HasTextBefore);
        Assert.True(snippet.HasTextAfter);
        Assert.Equal(text.Substring(snippet.Offset, snippet.Text.Length), snippet.Text);
        Assert.StartsWith("тут", snippet.Text, StringComparison.Ordinal);
        Assert.EndsWith("текст", snippet.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PrefersWindowWithMoreDistinctQueryWords()
    {
        var filler = string.Join(' ', Enumerable.Repeat("текст", 30));
        var text = $"кава кава кава {filler} кава і молоко {filler}";

        var snippet = SnippetBuilder.Build(ChunkOf(text), "кава молоко", maxLength: 50);

        Assert.Contains("молоко", Highlighted(snippet));
    }

    [Fact]
    public void Build_QueryOfOnlyStopWords_HasNoHighlights()
    {
        var snippet = SnippetBuilder.Build(ChunkOf("і та на"), "і та");

        Assert.Empty(snippet.Highlights);
    }

    [Theory]
    [InlineData("кава", "кав")]
    [InlineData("нотатки", "нотат")]
    [InlineData("api", "api")]
    [InlineData("docker", "dock")]
    public void Stem_KeepsWordRoot(string word, string expected)
    {
        Assert.Equal(expected, SearchTerms.Stem(word));
    }
}
