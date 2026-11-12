using System.IO.Compression;
using LocalMind.Core.Configuration;
using LocalMind.Core.Errors;

namespace LocalMind.Core.Ingestion;

/// <summary>
/// Перевірки вхідного файла перед парсингом, спільні для всіх шляхів індексації: розмір, сигнатура формату
/// і захист DOCX від zip-бомб. Порушення — <see cref="DocumentParseException"/>: документ отримує статус помилки
/// з цим повідомленням, а індексація решти файлів триває.
/// </summary>
public static class FileGuard
{
    /// <summary>PDF-читачі приймають заголовок <c>%PDF-</c> у перших 1024 байтах (специфікація допускає сміття перед ним).</summary>
    private const int PdfHeaderWindow = 1024;

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>Перевіряє розмір файла до того, як його читати.</summary>
    /// <exception cref="DocumentParseException">Файл більший за <see cref="IngestionOptions.MaxFileSizeBytes"/>.</exception>
    public static void CheckSize(long length, IngestionOptions limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (length > limits.MaxFileSizeBytes)
        {
            throw new DocumentParseException(
                $"Файл завеликий: {FormatMegabytes(length)} МБ, ліміт — {FormatMegabytes(limits.MaxFileSizeBytes)} МБ.");
        }
    }

    /// <summary>
    /// Перевіряє вміст за розширенням: PDF має починатися з <c>%PDF-</c>, DOCX — бути коректним zip-архівом
    /// у межах лімітів. Для решти форматів (текст, Markdown) сигнатури немає. Порожній файл не перевіряється:
    /// парсери самі вважають його порожнім документом. Позиція потоку після перевірки повертається на початок.
    /// </summary>
    /// <param name="extension">Розширення файла з крапкою, у будь-якому регістрі.</param>
    /// <param name="content">Потік з підтримкою позиціонування.</param>
    /// <param name="limits">Ліміти.</param>
    /// <exception cref="DocumentParseException">Вміст не відповідає розширенню або перевищує ліміти.</exception>
    public static void CheckContent(string extension, Stream content, IngestionOptions limits)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(limits);

        if (!content.CanSeek || content.Length == 0)
        {
            return;
        }

        content.Position = 0;
        try
        {
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                CheckPdf(content);
            }
            else if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                CheckDocx(content, limits);
            }
        }
        finally
        {
            content.Position = 0;
        }
    }

    private static void CheckPdf(Stream content)
    {
        var header = new byte[PdfHeaderWindow];
        var read = ReadFully(content, header);

        if (header.AsSpan(0, read).IndexOf(PdfSignature) < 0)
        {
            throw new DocumentParseException("Файл має розширення .pdf, але не є PDF: немає сигнатури %PDF.");
        }
    }

    private static void CheckDocx(Stream content, IngestionOptions limits)
    {
        var header = new byte[ZipSignature.Length];
        if (ReadFully(content, header) < header.Length || !header.AsSpan().SequenceEqual(ZipSignature))
        {
            throw new DocumentParseException("Файл має розширення .docx, але не є DOCX: це не zip-архів.");
        }

        content.Position = 0;
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > limits.MaxArchiveEntries)
            {
                throw new DocumentParseException(
                    $"DOCX містить забагато записів: {archive.Entries.Count}, ліміт — {limits.MaxArchiveEntries}.");
            }

            CountUncompressed(archive, limits.MaxArchiveUncompressedBytes);
        }
        catch (InvalidDataException exception)
        {
            throw new DocumentParseException("Файл пошкоджений або не відповідає формату DOCX.", exception);
        }
    }

    /// <summary>
    /// Розпаковує записи в нікуди й рахує фактичний розмір: заявлений у заголовку розмір підробляється,
    /// тож на нього покладатися не можна. Зупиняється, щойно ліміт перевищено, тож бомба не розгортається.
    /// </summary>
    private static void CountUncompressed(ZipArchive archive, long limit)
    {
        var buffer = new byte[81_920];
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > limit)
                {
                    throw new DocumentParseException(
                        $"DOCX після розпакування перевищує {FormatMegabytes(limit)} МБ: схоже на zip-бомбу.");
                }
            }
        }
    }

    private static int ReadFully(Stream stream, byte[] buffer)
    {
        var total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }

        return total;
    }

    private static string FormatMegabytes(long bytes) =>
        (bytes / 1024.0 / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
