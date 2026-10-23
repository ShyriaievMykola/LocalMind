namespace LocalMind.Core.Models;

/// <summary>Стан фонової індексації — для відображення прогресу.</summary>
public sealed record IndexingProgress
{
    /// <param name="processed">Скільки файлів уже оброблено (успішно або з помилкою).</param>
    /// <param name="total">Скільки файлів у черзі загалом.</param>
    /// <param name="failed">Скільки з оброблених файлів завершились помилкою.</param>
    /// <param name="currentFile">Файл, що обробляється зараз, або <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Від'ємні значення, <paramref name="processed"/> більше за <paramref name="total"/>
    /// або <paramref name="failed"/> більше за <paramref name="processed"/>.
    /// </exception>
    public IndexingProgress(int processed, int total, int failed = 0, string? currentFile = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        ArgumentOutOfRangeException.ThrowIfNegative(processed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(processed, total);
        ArgumentOutOfRangeException.ThrowIfNegative(failed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(failed, processed);

        Processed = processed;
        Total = total;
        Failed = failed;
        CurrentFile = currentFile;
    }

    /// <summary>Скільки файлів уже оброблено.</summary>
    public int Processed { get; }

    /// <summary>Скільки файлів у черзі загалом.</summary>
    public int Total { get; }

    /// <summary>Скільки оброблених файлів завершились помилкою.</summary>
    public int Failed { get; }

    /// <summary>Файл, що обробляється зараз.</summary>
    public string? CurrentFile { get; }

    /// <summary>Частка виконаного від 0 до 1; порожня черга вважається виконаною.</summary>
    public double Fraction => Total == 0 ? 1.0 : (double)Processed / Total;

    /// <summary>Чи оброблено всі файли.</summary>
    public bool IsCompleted => Processed == Total;
}
