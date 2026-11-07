using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using Microsoft.Extensions.Options;

namespace LocalMind.Core.Ingestion.Chunking;

/// <summary>Ріже текст на фрагменти заданого розміру в токенах із перекриттям.</summary>
public sealed class FixedSizeChunker : IChunker
{
    private readonly ChunkingOptions _options;
    private readonly ITokenCounter _tokenCounter;

    public FixedSizeChunker(IOptions<ChunkingOptions> options, ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenCounter);

        _options = options.Value;
        _tokenCounter = tokenCounter;
    }

    /// <inheritdoc />
    public IReadOnlyList<Chunk> Split(Guid documentId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var words = GetWords(text);
        var chunks = new List<Chunk>();
        var startWordIndex = 0;

        while (startWordIndex < words.Count)
        {
            var endWordIndex = startWordIndex;

            for (var wordIndex = startWordIndex; wordIndex < words.Count; wordIndex++)
            {
                var candidate = text[words[startWordIndex].Start..words[wordIndex].End];
                if (_tokenCounter.CountTokens(candidate) > _options.ChunkSizeTokens)
                {
                    break;
                }

                endWordIndex = wordIndex;

                if (wordIndex == words.Count - 1)
                {
                    break;
                }
            }

            var firstWord = words[startWordIndex];
            var lastWord = words[endWordIndex];
            var chunkText = text[firstWord.Start..lastWord.End];
            chunks.Add(new Chunk(Guid.NewGuid(), documentId, chunks.Count, chunkText, firstWord.Start));

            if (endWordIndex == words.Count - 1)
            {
                break;
            }

            var nextStartWordIndex = endWordIndex + 1;
            var overlapTokenCount = (int)((long)_options.ChunkSizeTokens * _options.OverlapPercent / 100);
            if (overlapTokenCount > 0)
            {
                for (var overlapStartWordIndex = endWordIndex;
                     overlapStartWordIndex > startWordIndex;
                     overlapStartWordIndex--)
                {
                    var overlapText = text[words[overlapStartWordIndex].Start..lastWord.End];
                    if (_tokenCounter.CountTokens(overlapText) > overlapTokenCount)
                    {
                        break;
                    }

                    nextStartWordIndex = overlapStartWordIndex;
                }
            }

            startWordIndex = nextStartWordIndex;
        }

        return chunks;
    }

    private static List<WordRange> GetWords(string text)
    {
        var words = new List<WordRange>();
        var wordStart = -1;

        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                if (wordStart >= 0)
                {
                    words.Add(new WordRange(wordStart, index));
                    wordStart = -1;
                }
            }
            else if (wordStart < 0)
            {
                wordStart = index;
            }
        }

        if (wordStart >= 0)
        {
            words.Add(new WordRange(wordStart, text.Length));
        }

        return words;
    }

    private readonly record struct WordRange(int Start, int End);
}