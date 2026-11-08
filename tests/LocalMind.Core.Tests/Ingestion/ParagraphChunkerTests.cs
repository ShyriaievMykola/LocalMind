using LocalMind.Core.Configuration;
using LocalMind.Core.Ingestion.Chunking;
using LocalMind.TestUtilities;
using Microsoft.Extensions.Options;

namespace LocalMind.Core.Tests.Ingestion;

public class ParagraphChunkerTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();

    private static ParagraphChunker CreateChunker(int chunkSizeTokens, int overlapPercent = 0)
    {
        var options = Options.Create(new ChunkingOptions
        {
            ChunkSizeTokens = chunkSizeTokens,
            OverlapPercent = overlapPercent
        });

        return new ParagraphChunker(options, new FakeTokenCounter());
    }

    [Fact]
    public void Split_MergesAdjacentParagraphsWithinTokenLimit()
    {
        const string text = "one two\n\nthree four\n\nfive six seven";
        var chunks = CreateChunker(4).Split(DocumentId, text);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("one two\n\nthree four", chunks[0].Text);
        Assert.Equal("five six seven", chunks[1].Text);
        Assert.Equal(text.IndexOf("five", StringComparison.Ordinal), chunks[1].StartOffset);
    }

    [Fact]
    public void Split_HeadingStaysWithTheFirstParagraphInItsSection()
    {
        const string text = "before section\n\n## Section title\n\nfirst paragraph\n\nsecond paragraph";
        var chunks = CreateChunker(4).Split(DocumentId, text);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("before section", chunks[0].Text);
        Assert.Equal("## Section title\n\nfirst paragraph", chunks[1].Text);
        Assert.Equal("second paragraph", chunks[2].Text);
        Assert.Equal(text.IndexOf("## Section title", StringComparison.Ordinal), chunks[1].StartOffset);
    }

    [Fact]
    public void Split_SetextHeadingStaysWithTheFirstParagraph()
    {
        const string text = "Section title\n-------------\n\nfirst paragraph";
        var chunks = CreateChunker(4).Split(DocumentId, text);

        var chunk = Assert.Single(chunks);
        Assert.Equal(text, chunk.Text);
        Assert.Equal(0, chunk.StartOffset);
    }

    [Fact]
    public void Split_LongParagraphUsesFixedSizeChunkerAndPreservesOffsets()
    {
        const string text = "one two three four five";
        var chunks = CreateChunker(2).Split(DocumentId, text);

        Assert.Equal(["one two", "three four", "five"], chunks.Select(chunk => chunk.Text));
        Assert.Equal(
            [0, text.IndexOf("three", StringComparison.Ordinal), text.IndexOf("five", StringComparison.Ordinal)],
            chunks.Select(chunk => chunk.StartOffset));
    }

    [Fact]
    public void Split_ChunkOrdinalsAreSequentialAndOffsetsReferenceSourceText()
    {
        const string text = "# Heading\n\nshort paragraph\n\nanother paragraph";
        var chunks = CreateChunker(3).Split(DocumentId, text);

        for (var index = 0; index < chunks.Count; index++)
        {
            Assert.Equal(index, chunks[index].Ordinal);
            Assert.StartsWith(chunks[index].Text, text[chunks[index].StartOffset..], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void Split_EmptyOrWhitespaceText_ReturnsNoChunks(string text)
    {
        Assert.Empty(CreateChunker(4).Split(DocumentId, text));
    }
}
