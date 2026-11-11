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
                var word = text[start..i].ToLowerInvariant();
                if (word.Length >= 2 && !StopWords.Contains(word) && !terms.Contains(word, StringComparer.Ordinal))
                {
                    terms.Add(word);
                }

                start = -1;
            }
        }

        return terms;
    }

    /// <summary>
    /// Спільний початок, за яким шукаємо форми слова: до 3 літер — слово повністю, 4–5 — без останньої літери,
    /// довше — без двох останніх, але не коротше за 4: «кава» → «кав», «нотатки» → «нотат».
    /// </summary>
    public static string Stem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return word.Length switch
        {
            <= 3 => word,
            <= 5 => word[..^1],
            _ => word[..Math.Max(4, word.Length - 2)],
        };
    }

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
