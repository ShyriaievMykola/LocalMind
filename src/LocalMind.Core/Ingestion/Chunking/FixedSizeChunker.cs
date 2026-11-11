using System.Globalization;
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
            var first = words[startWordIndex];
            var endWordIndex = FindLast(
                startWordIndex,
                words.Count - 1,
                index => _tokenCounter.CountTokens(text[first.Start..words[index].End]) <= _options.ChunkSizeTokens);
            var fits = endWordIndex >= startWordIndex;

            if (!fits)
            {
                // Ріжемо слово, поки решта не вміститься; решта стає першим словом наступного фрагмента.
                // Перекриття тут не потрібне. Решту цілком не токенізуємо — інакше нарізка квадратична.
                var word = words[startWordIndex];
                int cut;
                while ((cut = FindLongestFittingEnd(text, word)) < word.End)
                {
                    chunks.Add(new Chunk(Guid.NewGuid(), documentId, chunks.Count, text[word.Start..cut], word.Start));
                    word = word with { Start = cut };
                }

                words[startWordIndex] = word;
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

    /// <summary>
    /// Кінець найдовшого початку слова, що вміщується у фрагмент, по межі графеми (емодзі з ZWJ, літера з
    /// комбінуючим знаком не розриваються); щонайменше одна графема. <c>word.End</c> — слово вміщується повністю.
    /// </summary>
    private int FindLongestFittingEnd(string text, WordRange word)
    {
        var end = FindLast(
            word.Start + 1,
            word.End,
            index => _tokenCounter.CountTokens(text[word.Start..index]) <= _options.ChunkSizeTokens);

        // Відступаємо до межі графеми. Йдемо від початку слова, але лише до end — це розмір одного фрагмента.
        var boundary = word.Start + StringInfo.GetNextTextElementLength(text.AsSpan(word.Start, word.End - word.Start));
        var position = boundary;
        while (position < word.End)
        {
            var next = position + StringInfo.GetNextTextElementLength(text.AsSpan(position, word.End - position));
            if (next > end)
            {
                break;
            }

            position = next;
        }

        return Math.Max(position, boundary);
    }

    /// <summary>
    /// Найбільший індекс з [<paramref name="first"/>, <paramref name="last"/>], для якого <paramref name="fits"/>;
    /// якщо жодного — <c>first - 1</c>. Умова монотонна: спочатку виконується, далі — ні.
    /// Спершу галоп (first, +1, +3, +7…), потім бінарний пошук: кожна перевірка рахує текст не довший
    /// за два фрагменти, тож нарізка лінійна навіть для мегабайтного тексту чи слова.
    /// </summary>
    private static int FindLast(int first, int last, Func<int, bool> fits)
    {
        var good = first - 1;
        var bad = last + 1;
        var step = 1;
        var probe = first;
        while (probe <= last)
        {
            if (!fits(probe))
            {
                bad = probe;
                break;
            }

            good = probe;
            if (probe == last)
            {
                return good;
            }

            probe = (int)Math.Min((long)good + step, last);
            step *= 2;
        }

        var low = good + 1;
        var high = bad - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (fits(middle))
            {
                good = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return good;
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