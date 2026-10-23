namespace LocalMind.Core.Models;

/// <summary>Знайдений фрагмент разом із джерелом та оцінкою релевантності.</summary>
public sealed record SearchResult
{
    /// <param name="chunk">Знайдений фрагмент.</param>
    /// <param name="documentPath">Шлях до файла-джерела.</param>
    /// <param name="score">Оцінка релевантності: чим більша, тим краще; шкала залежить від режиму пошуку.</param>
    /// <exception cref="ArgumentException">Порушено одне з обмежень аргументів.</exception>
    public SearchResult(Chunk chunk, string documentPath, double score)
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
    }

    /// <summary>Знайдений фрагмент.</summary>
    public Chunk Chunk { get; }

    /// <summary>Шлях до файла-джерела.</summary>
    public string DocumentPath { get; }

    /// <summary>Оцінка релевантності.</summary>
    public double Score { get; }
}
