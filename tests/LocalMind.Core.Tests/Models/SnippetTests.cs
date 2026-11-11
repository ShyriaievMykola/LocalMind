using LocalMind.Core.Models;

namespace LocalMind.Core.Tests.Models;

public class SnippetTests
{
    [Fact]
    public void Constructor_ValidHighlights_SetsProperties()
    {
        var snippet = new Snippet("кава зранку", 5, [new HighlightRange(0, 4)], hasTextBefore: true);

        Assert.Equal("кава зранку", snippet.Text);
        Assert.Equal(5, snippet.Offset);
        Assert.Equal(4, Assert.Single(snippet.Highlights).End);
        Assert.True(snippet.HasTextBefore);
        Assert.False(snippet.HasTextAfter);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 2)]
    [InlineData(8, 5)]
    public void Constructor_HighlightOutsideTextOrEmpty_Throws(int start, int length)
    {
        Assert.Throws<ArgumentException>(() => new Snippet("кава зранку", 0, [new HighlightRange(start, length)]));
    }

    [Fact]
    public void Constructor_OverlappingHighlights_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new Snippet("кава зранку", 0, [new HighlightRange(0, 4), new HighlightRange(2, 3)]));
    }

    [Fact]
    public void SearchResult_WithoutSnippet_UsesWholeChunk()
    {
        var chunk = new Chunk(Guid.NewGuid(), Guid.NewGuid(), 0, "текст фрагмента", 0);

        var result = new SearchResult(chunk, "/a.md", 1.0);

        Assert.Equal(chunk.Text, result.Snippet.Text);
        Assert.Empty(result.Snippet.Highlights);
    }
}
