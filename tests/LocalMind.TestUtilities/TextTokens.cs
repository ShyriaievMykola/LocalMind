namespace LocalMind.TestUtilities;

/// <summary>Спрощена токенізація для фейків: слова з літер і цифр у нижньому регістрі.</summary>
public static class TextTokens
{
    /// <summary>Розбиває текст на слова.</summary>
    public static IEnumerable<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                yield return text[start..i].ToLowerInvariant();
                start = -1;
            }
        }
    }
}
