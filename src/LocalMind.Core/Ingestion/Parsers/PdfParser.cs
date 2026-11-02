using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Core;

namespace LocalMind.Core.Ingestion.Parsers;

public sealed class PdfParser : IDocumentParser
{
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    public async Task<ParsedDocument> ParseAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        cancellationToken.ThrowIfCancellationRequested();

        if (content.CanSeek && content.Length == 0)
        {
            return new ParsedDocument(string.Empty);
        }

        try
        {
            using var memoryStream = new MemoryStream();
            await content.CopyToAsync(memoryStream, cancellationToken);

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

                var pageText = page.Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    pageTexts.Add(pageText.Trim());
                }
            }

            if (pageTexts.Count == 0)
            {
                return new ParsedDocument(string.Empty, title);
            }

            var text = string.Join("\n\n", pageTexts).Replace("\r\n", "\n").Trim();

            return new ParsedDocument(text, title);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new DocumentParseException("Файл зашифровано і його неможливо прочитати без пароля.", ex);
        }
        catch (Exception ex) when (ex is PdfDocumentFormatException or FormatException or InvalidOperationException)
        {
            throw new DocumentParseException("Файл пошкоджений або не відповідає формату PDF.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not DocumentParseException)
        {
            throw new DocumentParseException($"Помилка читання PDF-документа: {ex.Message}", ex);
        }
    }
}
