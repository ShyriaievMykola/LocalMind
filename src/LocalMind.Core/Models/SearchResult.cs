namespace LocalMind.Core.Models;

/// <summary>Знайдений фрагмент разом із джерелом та оцінкою релевантності.</summary>
public sealed record SearchResult
{
    /// <param name="chunk">Знайдений фрагмент.</param>
    /// <param name="documentPath">Шлях до файла-джерела.</param>
    /// <param name="score">Оцінка релевантності: чим більша, тим краще; шкала залежить від режиму пошуку.</param>
    /// <param name="snippet">Уривок для картки результату; за замовчуванням — увесь фрагмент без підсвітки.</param>
    /// <exception cref="ArgumentException">Порушено одне з обмежень аргументів.</exception>
    public SearchResult(Chunk chunk, string documentPath, double score, Snippet? snippet = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        if (!double.IsFinite(score))
        {
            throw new ArgumentOutOfRangeException(nameof(score), score, "Оцінка має бути скінченним числом.");
        }

        Chunk = chunk;
        DocumentPath = documentPath;
        Score = score;
        Snippet = snippet ?? Snippet.Whole(chunk);
    }

    /// <summary>Знайдений фрагмент.</summary>
    public Chunk Chunk { get; }

    /// <summary>Шлях до файла-джерела.</summary>
    public string DocumentPath { get; }

    /// <summary>
    /// Оцінка релевантності: чим більша, тим краще. Шкала залежить від режиму: косинусна схожість (Semantic),
    /// BM25 (Keyword) або RRF, приблизно 0.01–0.03 (Hybrid). Порівнювати оцінки можна лише в межах одного режиму,
    /// а надійно — в межах одного запиту. UI показує їх відносно (ранг або частка від найкращої), без абсолютних порогів.
    /// </summary>
    public double Score { get; }

    /// <summary>Уривок фрагмента з підсвіткою слів запиту — те, що показує картка результату.</summary>
    public Snippet Snippet { get; }
}
