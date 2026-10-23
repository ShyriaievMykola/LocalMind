namespace LocalMind.Core.Models;

/// <summary>Пошуковий запит користувача.</summary>
public sealed record SearchQuery
{
    /// <summary>Кількість результатів за замовчуванням.</summary>
    public const int DefaultTopK = 10;

    /// <param name="text">Текст запиту природною мовою.</param>
    /// <param name="mode">Режим пошуку.</param>
    /// <param name="topK">Скільки результатів повернути, більше нуля.</param>
    /// <exception cref="ArgumentException">Порожній запит, невідомий режим або <paramref name="topK"/> ≤ 0.</exception>
    public SearchQuery(string text, SearchMode mode = SearchMode.Hybrid, int topK = DefaultTopK)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Невідомий режим пошуку.");
        }

        Text = text.Trim();
        Mode = mode;
        TopK = topK;
    }

    /// <summary>Текст запиту без пробілів на початку й у кінці.</summary>
    public string Text { get; }

    /// <summary>Режим пошуку.</summary>
    public SearchMode Mode { get; }

    /// <summary>Скільки результатів повернути.</summary>
    public int TopK { get; }
}
