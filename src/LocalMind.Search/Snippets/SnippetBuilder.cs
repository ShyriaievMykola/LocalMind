using LocalMind.Core.Models;

namespace LocalMind.Search.Snippets;

/// <summary>
/// Вибирає з фрагмента уривок для картки результату і позначає слова запиту для підсвітки.
/// Слово фрагмента збігається зі словом запиту без урахування регістру, якщо має з ним спільний початок
/// (<see cref="Stem"/>) — так «каву» знаходиться за запитом «кава», а «нотатках» — за «нотатки».
/// Стоп-слова («і», «the», «на»…) не підсвічуються.
/// </summary>
public static class SnippetBuilder
{
    /// <summary>Довжина уривка за замовчуванням, символів.</summary>
    public const int DefaultMaxLength = 300;

    /// <summary>Скільки контексту лишати перед першим збігом, частка від довжини уривка.</summary>
    private const int ContextBeforeDivisor = 5;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // Українська.
        "і", "й", "та", "а", "але", "в", "у", "з", "із", "зі", "на", "до", "від", "по", "за", "про", "для", "що", "як",
        "це", "не", "чи", "або", "о", "об", "при", "під", "над", "між", "так", "же", "би", "б", "ж", "то", "той", "ця",
        // Англійська.
        "a", "an", "the", "and", "or", "of", "to", "in", "on", "for", "is", "are", "be", "with", "by", "at", "as", "it",
        "this", "that", "from", "how", "what",
    };

    /// <summary>Будує уривок фрагмента для запиту.</summary>
    /// <param name="chunk">Знайдений фрагмент.</param>
    /// <param name="query">Текст запиту.</param>
    /// <param name="maxLength">Найбільша довжина уривка, символів; довший фрагмент обрізається по межі слова.</param>
    public static Snippet Build(Chunk chunk, string query, int maxLength = DefaultMaxLength)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        var text = chunk.Text;
        var stems = QueryStems(query);
        var matches = FindMatches(text, stems);

        var (start, end) = text.Length <= maxLength
            ? (0, text.Length)
            : ChooseWindow(text, matches, maxLength);
        (start, end) = TrimWhitespace(text, start, end);

        var highlights = matches
            .Where(m => m.Start >= start && m.End <= end)
            .Select(m => new HighlightRange(m.Start - start, m.Length))
            .ToList();

        return new Snippet(
            text[start..end],
            start,
            highlights,
            hasTextBefore: !string.IsNullOrWhiteSpace(text[..start]),
            hasTextAfter: !string.IsNullOrWhiteSpace(text[end..]));
    }

    /// <summary>
    /// Спільний початок, за яким шукаємо форми слова: до 3 літер — слово повністю, 4–5 — без останньої літери,
    /// довше — без двох останніх, але не коротше за 4. Закінчення відкидаємо, корінь лишаємо:
    /// «кава» → «кав» (каву, кави), «нотатки» → «нотат» (нотаток, нотатках).
    /// </summary>
    internal static string Stem(string word) => word.Length switch
    {
        <= 3 => word,
        <= 5 => word[..^1],
        _ => word[..Math.Max(4, word.Length - 2)],
    };

    private static List<string> QueryStems(string query)
    {
        var stems = new List<string>();
        foreach (var (start, length) in Words(query))
        {
            var word = query.Substring(start, length).ToLowerInvariant();
            if (word.Length < 2 || StopWords.Contains(word))
            {
                continue;
            }

            var stem = Stem(word);
            if (!stems.Contains(stem, StringComparer.Ordinal))
            {
                stems.Add(stem);
            }
        }

        return stems;
    }

    private static List<Match> FindMatches(string text, List<string> stems)
    {
        var matches = new List<Match>();
        if (stems.Count == 0)
        {
            return matches;
        }

        foreach (var (start, length) in Words(text))
        {
            var word = text.AsSpan(start, length);
            for (var i = 0; i < stems.Count; i++)
            {
                if (word.StartsWith(stems[i], StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new Match(start, length, i));
                    break;
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Вікно з найбільшою кількістю різних слів запиту (за рівності — більше збігів, потім раніше).
    /// Без збігів — початок фрагмента.
    /// </summary>
    private static (int Start, int End) ChooseWindow(string text, List<Match> matches, int maxLength)
    {
        var bestStart = 0;
        var bestDistinct = 0;
        var bestCount = 0;
        var contextBefore = maxLength / ContextBeforeDivisor;

        foreach (var anchor in matches)
        {
            var start = Math.Max(0, anchor.Start - contextBefore);
            var inWindow = matches.Where(m => m.Start >= start && m.End <= start + maxLength).ToList();
            var distinct = inWindow.Select(m => m.StemIndex).Distinct().Count();
            if (distinct > bestDistinct || (distinct == bestDistinct && inWindow.Count > bestCount))
            {
                (bestStart, bestDistinct, bestCount) = (start, distinct, inWindow.Count);
            }
        }

        var firstMatch = matches.FirstOrDefault(m => m.Start >= bestStart);
        var snappedStart = SnapStartToWord(text, bestStart, firstMatch.Length > 0 ? firstMatch.Start : bestStart + maxLength);
        return (snappedStart, SnapEndToWord(text, snappedStart, snappedStart + maxLength));
    }

    /// <summary>Якщо початок посеред слова — переносимо на наступне слово, але не далі за перший збіг.</summary>
    private static int SnapStartToWord(string text, int start, int limit)
    {
        if (start == 0 || char.IsWhiteSpace(text[start - 1]) || char.IsWhiteSpace(text[start]))
        {
            return start;
        }

        var next = start;
        while (next < text.Length && next < limit && !char.IsWhiteSpace(text[next]))
        {
            next++;
        }

        return next < limit ? next : start;
    }

    /// <summary>Кінець не посеред слова: відступаємо до останнього пробілу; якщо його немає — ріжемо як є.</summary>
    private static int SnapEndToWord(string text, int start, int end)
    {
        if (end >= text.Length)
        {
            return text.Length;
        }

        if (char.IsWhiteSpace(text[end]) || char.IsWhiteSpace(text[end - 1]))
        {
            return end;
        }

        var space = text.LastIndexOfAny([' ', '\t', '\n', '\r'], end - 1, end - start);
        if (space > start)
        {
            return space;
        }

        // Одне довге слово без пробілів: не розриваємо сурогатну пару.
        return char.IsLowSurrogate(text[end]) ? end - 1 : end;
    }

    private static (int Start, int End) TrimWhitespace(string text, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return (start, end);
    }

    private static IEnumerable<(int Start, int Length)> Words(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWord = i < text.Length && IsWordChar(text, i);
            if (isWord && start < 0)
            {
                start = i;
            }
            else if (!isWord && start >= 0)
            {
                yield return (start, i - start);
                start = -1;
            }
        }
    }

    /// <summary>Літера, цифра або апостроф між літерами («м'ята», «п’ять»), але не лапки навколо слова.</summary>
    private static bool IsWordChar(string text, int index)
    {
        var c = text[index];
        if (char.IsLetterOrDigit(c))
        {
            return true;
        }

        return c is '\'' or '’' or 'ʼ'
            && index > 0 && char.IsLetter(text[index - 1])
            && index + 1 < text.Length && char.IsLetter(text[index + 1]);
    }

    private readonly record struct Match(int Start, int Length, int StemIndex)
    {
        public int End => Start + Length;
    }
}
