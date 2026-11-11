using LocalMind.Core.Abstractions;
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

    [Fact]
    public void Split_UsesLogarithmicTokenCounterCallsToFindChunkBoundary()
    {
        var text = string.Join(' ', Enumerable.Range(0, 1024).Select(index => $"word{index}"));
        var tokenCounter = new CountingTokenCounter();
        var chunker = new FixedSizeChunker(
            Options.Create(new ChunkingOptions { ChunkSizeTokens = 64, OverlapPercent = 0 }),
            tokenCounter);

        var chunks = chunker.Split(DocumentId, text);

        Assert.Equal(16, chunks.Count);
        // Галоп + бінарний пошук: ~2·log₂(розмір фрагмента) викликів на фрагмент.
        Assert.InRange(tokenCounter.CallCount, 1, chunks.Count * 15);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 51)]
    public void Constructor_InvalidOptions_ThrowsOptionsValidationException(int chunkSizeTokens, int overlapPercent)
    {
        var options = Options.Create(new ChunkingOptions
        {
            ChunkSizeTokens = chunkSizeTokens,
            OverlapPercent = overlapPercent
        });

        Assert.Throws<OptionsValidationException>(() => new FixedSizeChunker(options, new FakeTokenCounter()));
    }

    private sealed class CountingTokenCounter : ITokenCounter
    {
        public int CallCount { get; private set; }

        public int CountTokens(string text)
        {
            CallCount++;
            return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }

    [Fact]
    public void Split_WordLongerThanChunk_IsCutByCharactersWithoutLosingText()
    {
        const string text = "ab abcdefghij cd";
        var options = Options.Create(new ChunkingOptions { ChunkSizeTokens = 4, OverlapPercent = 0 });
        var chunker = new FixedSizeChunker(options, new CharTokenCounter());

        var chunks = chunker.Split(DocumentId, text);

        Assert.Equal(["ab", "abcd", "efgh", "ij", "cd"], chunks.Select(c => c.Text));
        Assert.All(chunks, c => Assert.Equal(c.Text, text.Substring(c.StartOffset, c.Text.Length)));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Ordinal));
    }

    [Fact]
    public void Split_LongWordWithSurrogatePairs_DoesNotBreakPairs()
    {
        const string text = "😀😀😀"; // 6 UTF-16 символів
        var options = Options.Create(new ChunkingOptions { ChunkSizeTokens = 3, OverlapPercent = 0 });
        var chunker = new FixedSizeChunker(options, new CharTokenCounter());

        var chunks = chunker.Split(DocumentId, text);

        Assert.Equal(["😀", "😀", "😀"], chunks.Select(c => c.Text));
    }

    /// <summary>Токен — символ UTF-16 (разом із пробілами): довге слово не вміщується у фрагмент.</summary>
    private sealed class CharTokenCounter : ITokenCounter
    {
        public int CountTokens(string text) => text.Length;
    }

    [Fact]
    public void Split_HugeWordAndHugeText_TokenizeLinearAmountOfText()
    {
        var word = new string('x', 200_000);
        var text = word + " " + string.Join(' ', Enumerable.Repeat("слово", 40_000));
        var counter = new MeasuringTokenCounter();
        var chunker = new FixedSizeChunker(
            Options.Create(new ChunkingOptions { ChunkSizeTokens = 256, OverlapPercent = 15 }),
            counter);

        var chunks = chunker.Split(DocumentId, text);

        Assert.Equal(word.Length, chunks.Sum(c => c.Text.Count(ch => ch == 'x')));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 256));
        // Квадратична нарізка токенізувала б мільярди символів.
        Assert.InRange(counter.TotalCharacters, text.Length, text.Length * 50L);
    }

    [Fact]
    public void Split_LongWordWithCombiningMarks_DoesNotBreakGraphemes()
    {
        const string letter = "й"; // «й» у NFD: літера + комбінуючий знак
        var text = string.Concat(Enumerable.Repeat(letter, 5));
        var options = Options.Create(new ChunkingOptions { ChunkSizeTokens = 3, OverlapPercent = 0 });

        var chunks = new FixedSizeChunker(options, new CharTokenCounter()).Split(DocumentId, text);

        Assert.All(chunks, c => Assert.Equal(letter, c.Text));
    }

    /// <summary>Токен — символ; рахує, скільки тексту довелося токенізувати.</summary>
    private sealed class MeasuringTokenCounter : ITokenCounter
    {
        public long TotalCharacters { get; private set; }

        public int CountTokens(string text)
        {
            TotalCharacters += text.Length;
            return text.Length;
        }
    }
}
