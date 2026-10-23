namespace LocalMind.Core.Models;

/// <summary>Результат парсингу файла: чистий текст без розмітки.</summary>
public sealed record ParsedDocument
{
    /// <param name="text">Витягнутий текст; порожній рядок для порожнього файла.</param>
    /// <param name="title">Заголовок документа, якщо його вдалося визначити.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> дорівнює <see langword="null"/>.</exception>
    public ParsedDocument(string text, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
    }

    /// <summary>Витягнутий текст.</summary>
    public string Text { get; }

    /// <summary>Заголовок без зайвих пробілів або <see langword="null"/>, якщо його немає.</summary>
    public string? Title { get; }

    /// <summary>Чи містить документ хоч якийсь текст, крім пробілів.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}
