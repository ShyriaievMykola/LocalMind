using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace LocalMind.Core.Ingestion.Parsers;

public sealed class PdfParser : IDocumentParser
{
    private const long DefaultMaxFileSizeBytes = IngestionOptions.DefaultMaxFileSizeBytes;

    private readonly long _maxFileSizeBytes;

    /// <param name="maxFileSizeBytes">
    /// Страховка для потоків без відомої довжини; для файлів ліміт перевіряє <see cref="FileGuard"/> до читання,
    /// а DI передає сюди те саме значення з <see cref="IngestionOptions"/>.
    /// </param>
    public PdfParser(long maxFileSizeBytes = DefaultMaxFileSizeBytes)
    {
        if (maxFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileSizeBytes), "Розмір файлу повинен бути більшим за 0.");
        }

        _maxFileSizeBytes = maxFileSizeBytes;
    }

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    public async Task<ParsedDocument> ParseAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        cancellationToken.ThrowIfCancellationRequested();

        if (content.CanSeek)
        {
            if (content.Length == 0)
            {
                return new ParsedDocument(string.Empty);
            }

            if (content.Length > _maxFileSizeBytes)
            {
                throw TooLarge();
            }
        }

        try
        {
            using var memoryStream = new MemoryStream();
            await CopyWithLimitAsync(content, memoryStream, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (memoryStream.Length == 0)
            {
                return new ParsedDocument(string.Empty);
            }

            memoryStream.Position = 0;

            using var pdfDocument = PdfDocument.Open(memoryStream);

            cancellationToken.ThrowIfCancellationRequested();

            string? title = null;
            if (pdfDocument.Information is not null && !string.IsNullOrWhiteSpace(pdfDocument.Information.Title))
            {
                title = pdfDocument.Information.Title.Trim();
            }

            var pageTexts = new List<string>();

            foreach (var page in pdfDocument.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var words = page.GetWords();
                var pageText = words.Any() 
                    ? string.Join(" ", words.Select(w => w.Text)) 
                    : page.Text;

                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    pageTexts.Add(pageText.Trim());
                }
            }

            if (pageTexts.Count == 0)
            {
                return new ParsedDocument(string.Empty, title);
            }

            var text = string.Join("\n\n", pageTexts).Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            return new ParsedDocument(text, title);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new DocumentParseException("Файл зашифровано і його неможливо прочитати без пароля.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not DocumentParseException)
        {
            throw new DocumentParseException("Файл пошкоджений або не відповідає формату PDF.", ex);
        }
    }

    /// <summary>Копіює потік, але не більше ліміту: потік без довжини не з'їсть усю пам'ять.</summary>
    private async Task CopyWithLimitAsync(Stream source, MemoryStream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (destination.Length + read > _maxFileSizeBytes)
            {
                throw TooLarge();
            }

            destination.Write(buffer, 0, read);
        }
    }

    private DocumentParseException TooLarge() =>
        new($"Файл завеликий: більше {_maxFileSizeBytes} байт.");
}
