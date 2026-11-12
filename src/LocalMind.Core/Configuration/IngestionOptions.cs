namespace LocalMind.Core.Configuration;

/// <summary>
/// Ліміти для вхідних файлів, спільні для всіх парсерів: файл, що їх перевищує, не читається в пам'ять,
/// а отримує статус помилки. Секція конфігурації — <see cref="SectionName"/>.
/// </summary>
public sealed class IngestionOptions
{
    /// <summary>Секція конфігурації.</summary>
    public const string SectionName = "LocalMind:Ingestion";

    /// <summary>Розмір файла за замовчуванням, 50 МБ.</summary>
    public const long DefaultMaxFileSizeBytes = 50L * 1024 * 1024;

    /// <summary>Найбільший розмір одного файла, байт; більші файли не індексуються.</summary>
    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    /// <summary>
    /// Найбільша кількість записів у DOCX (це zip-архів). Звичайний документ має десятки записів;
    /// архів із сотнями тисяч порожніх записів — спроба вичерпати пам'ять.
    /// </summary>
    public int MaxArchiveEntries { get; set; } = 2000;

    /// <summary>
    /// Найбільший сумарний розмір розпакованого вмісту DOCX, байт. Рахується за фактично розпакованими даними,
    /// а не за розміром, заявленим в архіві: заявлене можна підробити (zip-бомба).
    /// </summary>
    public long MaxArchiveUncompressedBytes { get; set; } = 256L * 1024 * 1024;
}
