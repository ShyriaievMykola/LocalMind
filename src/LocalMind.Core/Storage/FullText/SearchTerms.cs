namespace LocalMind.Core.Storage.FullText;

/// <summary>
/// Слова запиту для keyword-пошуку: без регістру, без стоп-слів, з простим відкиданням закінчень
/// (<see cref="Stem"/>), щоб «кава» знаходила «каву», а «нотатки» — «нотатках».
/// </summary>
public static class SearchTerms
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // Українська.
        "і", "й", "та", "а", "але", "в", "у", "з", "із", "зі", "на", "до", "від", "по", "за", "про", "для", "що", "як",
        "це", "не", "чи", "або", "о", "об", "при", "під", "над", "між", "так", "же", "би", "б", "ж", "то", "той", "ця",
        // Англійська.
        "a", "an", "the", "and", "or", "of", "to", "in", "on", "for", "is", "are", "be", "with", "by", "at", "as", "it",
        "this", "that", "from", "how", "what",
    };

    /// <summary>Різні значущі слова тексту в нижньому регістрі, у порядку появи.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var terms = new List<string>();
        foreach (var (start, length) in WordRanges(text))
        {
            var word = text.Substring(start, length).ToLowerInvariant();
            if (word.Length >= 2 && !StopWords.Contains(word) && !terms.Contains(word, StringComparer.Ordinal))
            {
                terms.Add(word);
            }
        }

        return terms;
    }

    /// <summary>Межі слів у тексті: літери, цифри й апострофи між літерами.</summary>
    public static IEnumerable<(int Start, int Length)> WordRanges(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Enumerate(text);

        static IEnumerable<(int Start, int Length)> Enumerate(string text)
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
    }

    /// <summary>
    /// Спільний початок, за яким шукаємо форми слова: до 3 літер — слово повністю; 4–5 — без останньої літери, але лише
    /// якщо це голосна (закінчення відмінка: «кава» → «кав», «каву», «кави»); слово на приголосну лишається цілим, інакше
    /// «борщ» дав би «бор» і знаходив би «борозна»; довше — без двох останніх, але не коротше за 4: «нотатки» → «нотат».
    /// </summary>
    public static string Stem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return word.Length switch
        {
            <= 3 => word,
            <= 5 => IsVowelOrSign(word[^1]) ? word[..^1] : word,
            _ => word[..Math.Max(4, word.Length - 2)],
        };
    }

    /// <summary>Голосна (українська, російська, латиниця) або м'який знак: така остання літера — найімовірніше закінчення.</summary>
    private static bool IsVowelOrSign(char letter) => "аеєиіїоуюяыэёьaeiouy".Contains(char.ToLowerInvariant(letter), StringComparison.Ordinal);

    /// <summary>Літера, цифра або апостроф між літерами («м'ята», «п’ять»).</summary>
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
}
