using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;

namespace LocalMind.Core.Ingestion.Parsers;

public sealed class DocxParser : IDocumentParser
{
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".docx"
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
            using var memoryStream = content.CanSeek ? null : new MemoryStream();
            Stream documentStream = content;
            if (memoryStream is not null)
            {
                await content.CopyToAsync(memoryStream, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (memoryStream.Length == 0)
                {
                    return new ParsedDocument(string.Empty);
                }

                memoryStream.Position = 0;
                documentStream = memoryStream;
            }

            using var document = WordprocessingDocument.Open(documentStream, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                throw new DocumentParseException("DOCX-файл не містить основного тексту документа.");
            }

            var lines = new List<string>();
            AppendBlocks(body, lines, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var text = string.Join("\n", lines).Trim();
            var title = document.PackageProperties.Title;

            return new ParsedDocument(text, title);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not DocumentParseException)
        {
            throw new DocumentParseException("Файл пошкоджений або не відповідає формату DOCX.", exception);
        }
    }

    private static void AppendBlocks(
        OpenXmlElement container,
        ICollection<string> lines,
        CancellationToken cancellationToken)
    {
        foreach (var child in container.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (child)
            {
                case Paragraph paragraph:
                    var paragraphText = ExtractText(paragraph);
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        lines.Add(paragraphText);
                    }

                    break;
                case Table table:
                    AppendTable(table, lines, cancellationToken);
                    break;
                default:
                    AppendBlocks(child, lines, cancellationToken);
                    break;
            }
        }
    }

    private static void AppendTable(
        Table table,
        ICollection<string> lines,
        CancellationToken cancellationToken)
    {
        foreach (var row in table.Elements<TableRow>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cells = row.Elements<TableCell>()
                .Select(cell => string.Join(
                    " ",
                    cell.Descendants<Paragraph>()
                        .Select(ExtractText)
                        .Where(text => !string.IsNullOrWhiteSpace(text))));
            var rowText = string.Join("\t", cells);
            if (!string.IsNullOrWhiteSpace(rowText))
            {
                lines.Add(rowText);
            }
        }
    }

    private static string ExtractText(OpenXmlElement element)
    {
        var text = new StringBuilder();
        foreach (var child in element.Descendants())
        {
            switch (child)
            {
                case Text runText:
                    AppendNormalizedText(text, runText.Text);
                    break;
                case TabChar:
                    text.Append('\t');
                    break;
                case Break:
                case CarriageReturn:
                    text.Append('\n');
                    break;
            }
        }

        return text.ToString();
    }

    private static void AppendNormalizedText(StringBuilder builder, string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\r')
            {
                builder.Append('\n');
                if (index + 1 < value.Length && value[index + 1] == '\n')
                {
                    index++;
                }
            }
            else
            {
                builder.Append(value[index]);
            }
        }
    }
}
