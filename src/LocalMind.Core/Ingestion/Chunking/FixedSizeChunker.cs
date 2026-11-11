using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using Microsoft.Extensions.Options;

namespace LocalMind.Core.Ingestion.Chunking;

/// <summary>
/// Ріже текст на фрагменти заданого розміру в токенах із перекриттям. Слова не розриваються,
/// крім слова, яке саме не вміщується у фрагмент (довге посилання, base64): його ріжемо по символах,
/// інакше модель мовчки обрізала б кінець.
/// </summary>
public sealed class FixedSizeChunker : IChunker
{
    private readonly ChunkingOptions _options;
    private readonly ITokenCounter _tokenCounter;

    public FixedSizeChunker(IOptions<ChunkingOptions> options, ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenCounter);

        _options = options.Value;
        var validationResult = new ChunkingOptionsValidator().Validate(Options.DefaultName, _options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(ChunkingOptions),
                validationResult.Failures);
        }

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
            var fits = false;
            var low = startWordIndex;
            var high = words.Count - 1;
            while (low <= high)
            {
                var wordIndex = low + (high - low) / 2;
                var candidate = text[words[startWordIndex].Start..words[wordIndex].End];
                if (_tokenCounter.CountTokens(candidate) > _options.ChunkSizeTokens)
                {
                    high = wordIndex - 1;
                }
                else
                {
                    endWordIndex = wordIndex;
                    fits = true;
                    low = wordIndex + 1;
                }
            }

            if (!fits)
            {
                // Решта слова стає першим словом наступного фрагмента; перекриття тут не потрібне.
                var word = words[startWordIndex];
                var cut = FindLongestFittingEnd(text, word);
                chunks.Add(new Chunk(Guid.NewGuid(), documentId, chunks.Count, text[word.Start..cut], word.Start));
                words[startWordIndex] = word with { Start = cut };
                continue;
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

    /// <summary>Кінець найдовшого початку слова, що вміщується у фрагмент; щонайменше один символ.</summary>
    private int FindLongestFittingEnd(string text, WordRange word)
    {
        var end = NextCharEnd(text, word.Start);
        var low = end + 1;
        var high = word.End - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (_tokenCounter.CountTokens(text[word.Start..middle]) > _options.ChunkSizeTokens)
            {
                high = middle - 1;
            }
            else
            {
                end = middle;
                low = middle + 1;
            }
        }

        // Не розриваємо сурогатну пару (емодзі, рідкісні ієрогліфи): мінімальний кінець пару вже не розриває.
        return end < word.End && char.IsLowSurrogate(text[end]) ? end - 1 : end;
    }

    private static int NextCharEnd(string text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? index + 2 : index + 1;

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