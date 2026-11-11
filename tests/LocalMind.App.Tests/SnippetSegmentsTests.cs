using LocalMind.App.Controls;
using LocalMind.Core.Models;

namespace LocalMind.App.Tests;

public sealed class SnippetSegmentsTests
{
    [Fact]
    public void Create_NullSnippet_IsEmpty() => Assert.Empty(SnippetSegments.Create(null));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Create_NoHighlights_PreservesLiteralTextAndEllipses(bool before, bool after)
    {
        const string Text = "<тег> & \"лапки\"\nновий рядок";
        var segments = SnippetSegments.Create(new Snippet(Text, 0, [], before, after));

        Assert.Equal((before ? "…" : "") + Text + (after ? "…" : ""), string.Concat(segments.Select(s => s.Text)));
        Assert.All(segments, segment => Assert.False(segment.IsHighlighted));
    }

    [Fact]
    public void Create_RangesAreRelativeToSnippet_IgnoresChunkOffset()
    {
        var snippet = new Snippet("чай та кава", 200, [new(0, 3), new(7, 4)], hasTextBefore: true, hasTextAfter: true);

        var segments = SnippetSegments.Create(snippet);

        Assert.Equal("…чай та кава…", string.Concat(segments.Select(s => s.Text)));
        Assert.Equal(["чай", "кава"], segments.Where(s => s.IsHighlighted).Select(s => s.Text));
        Assert.Equal(" та ", segments[2].Text);
    }

    [Fact]
    public void Create_AdjacentRangesAndFullTextHighlight_PreserveAllCharacters()
    {
        var segments = SnippetSegments.Create(new Snippet("кавамолоко", 0, [new(0, 4), new(4, 6)]));

        Assert.Equal([new SnippetSegment("кава", true), new SnippetSegment("молоко", true)], segments);
    }

    [Fact]
    public void Create_UnicodeBeforeHighlight_UsesProvidedUtf16Offsets()
    {
        const string Text = "☕ 🫖 кава і чай";
        var snippet = new Snippet(Text, 0, [new(Text.IndexOf("кава", StringComparison.Ordinal), 4)]);

        var segments = SnippetSegments.Create(snippet);

        Assert.Equal(Text, string.Concat(segments.Select(s => s.Text)));
        Assert.Equal("кава", Assert.Single(segments, s => s.IsHighlighted).Text);
    }

    [Fact]
    public void Create_LongSnippet_PreservesRangesWithoutRecomputingMatches()
    {
        var text = new string('я', 5000) + "кава" + new string('ю', 5000);
        var segments = SnippetSegments.Create(new Snippet(text, 0, [new(5000, 4)]));

        Assert.Equal(text, string.Concat(segments.Select(s => s.Text)));
        Assert.Equal("кава", Assert.Single(segments, s => s.IsHighlighted).Text);
        Assert.Equal(3, segments.Count);
    }
}
