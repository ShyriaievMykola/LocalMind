namespace LocalMind.Core.Models;

/// <summary>Файл користувача в бібліотеці та результат його індексації.</summary>
public sealed record Document
{
    /// <summary>Довжина SHA-256 у шістнадцятковому записі.</summary>
    public const int ContentHashLength = 64;

    /// <param name="id">Ідентифікатор документа.</param>
    /// <param name="path">Повний шлях до файла.</param>
    /// <param name="contentHash">SHA-256 вмісту файла, 64 шістнадцяткові символи.</param>
    /// <param name="status">Результат індексації.</param>
    /// <param name="indexedAt">Час останньої індексації.</param>
    /// <param name="error">Причина помилки; вказується лише для <see cref="DocumentStatus.Error"/>.</param>
    /// <exception cref="ArgumentException">Порушено одне з обмежень аргументів.</exception>
    public Document(
        Guid id,
        string path,
        string contentHash,
        DocumentStatus status,
        DateTimeOffset indexedAt,
        string? error = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ідентифікатор документа не може бути порожнім.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contentHash);

        if (contentHash.Length != ContentHashLength || !contentHash.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException(
                $"Хеш вмісту має містити {ContentHashLength} шістнадцяткових символів.", nameof(contentHash));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Невідомий статус документа.");
        }

        if (status == DocumentStatus.Error && string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("Для статусу Error потрібно вказати причину помилки.", nameof(error));
        }

        if (status != DocumentStatus.Error && error is not null)
        {
            throw new ArgumentException("Причина помилки вказується лише для статусу Error.", nameof(error));
        }

        Id = id;
        Path = path;
        ContentHash = contentHash.ToLowerInvariant();
        Status = status;
        IndexedAt = indexedAt;
        Error = error;
    }

    /// <summary>Ідентифікатор документа.</summary>
    public Guid Id { get; }

    /// <summary>Повний шлях до файла.</summary>
    public string Path { get; }

    /// <summary>SHA-256 вмісту в нижньому регістрі — для виявлення змін.</summary>
    public string ContentHash { get; }

    /// <summary>Результат індексації.</summary>
    public DocumentStatus Status { get; }

    /// <summary>Час останньої індексації.</summary>
    public DateTimeOffset IndexedAt { get; }

    /// <summary>Причина помилки для <see cref="DocumentStatus.Error"/>, інакше <see langword="null"/>.</summary>
    public string? Error { get; }
}
