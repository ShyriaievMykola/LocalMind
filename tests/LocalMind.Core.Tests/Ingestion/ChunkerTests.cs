using LocalMind.Core.Configuration;
using LocalMind.Core.Ingestion.Chunking;
using LocalMind.TestUtilities;
using Microsoft.Extensions.Options;

namespace LocalMind.Core.Tests.Ingestion;

public class ChunkerTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly string[] WholeWordChunks = ["alpha", "beta", "gamma"];

    private static FixedSizeChunker CreateChunker(int chunkSizeTokens, int overlapPercent)
    {
        var options = Options.Create(new ChunkingOptions
        {
            ChunkSizeTokens = chunkSizeTokens,
            OverlapPercent = overlapPercent
        });

        return new FixedSizeChunker(options, new FakeTokenCounter());
    }

    [Fact]
    public void Split_TextShorterThanChunkSize_ReturnsOneChunk()
    {
        const string text = "one two three";
        var chunker = CreateChunker(4, 0);

        var chunks = chunker.Split(DocumentId, text);

        var chunk = Assert.Single(chunks);
        Assert.Equal(text, chunk.Text);
        Assert.Equal(0, chunk.Ordinal);
        Assert.Equal(0, chunk.StartOffset);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void Split_EmptyOrWhitespaceText_ReturnsNoChunks(string text)
    {
        var chunks = CreateChunker(4, 0).Split(DocumentId, text);

        Assert.Empty(chunks);
    }

    [Fact]
    public void Split_ZeroOverlap_DoesNotRepeatWords()
    {
        const string text = "one two three four five six";
        var chunks = CreateChunker(4, 0).Split(DocumentId, text);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("one two three four", chunks[0].Text);
        Assert.Equal("five six", chunks[1].Text);
        Assert.Equal(text.IndexOf("five", StringComparison.Ordinal), chunks[1].StartOffset);
    }

    [Fact]
    public void Split_FifteenPercentOverlap_RepeatsThreeTokens()
    {
        var words = Enumerable.Range(1, 25).Select(index => $"word{index}").ToArray();
        var text = string.Join(' ', words);
        var chunks = CreateChunker(20, 15).Split(DocumentId, text);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(string.Join(' ', words[..20]), chunks[0].Text);
        Assert.Equal(string.Join(' ', words[17..]), chunks[1].Text);
        Assert.Equal(text.IndexOf(words[17], StringComparison.Ordinal), chunks[1].StartOffset);
    }

    [Fact]
    public void Split_ChunkOffsetsPointToExactSourceText_AndOrdinalsAreSequential()
    {
        const string text = "alpha beta gamma delta epsilon";
        var chunks = CreateChunker(2, 0).Split(DocumentId, text);

        Assert.Equal(3, chunks.Count);
        for (var index = 0; index < chunks.Count; index++)
        {
            Assert.Equal(index, chunks[index].Ordinal);
            Assert.StartsWith(chunks[index].Text, text[chunks[index].StartOffset..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Split_DoesNotBreakWordsAtChunkBoundaries()
    {
        const string text = "alpha beta gamma";
        var chunks = CreateChunker(1, 0).Split(DocumentId, text);

        Assert.Equal(WholeWordChunks, chunks.Select(chunk => chunk.Text));
    }
}