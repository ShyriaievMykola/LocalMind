using System.Text;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;

namespace LocalMind.Core.Ingestion.Parsers;

/// <summary>
/// Реалізація <see cref="IDocumentParser"/> для парсингу простого тексту без розмітки.
/// </summary>
public sealed class PlainTextParser : IDocumentParser
{
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt"
    };

    public async Task<ParsedDocument> ParseAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.CanSeek)
        {
            if (content.Length == 0)
            {
                return new ParsedDocument(string.Empty);
            }
        }

        using var memoryStream = new MemoryStream();
        await content.CopyToAsync(memoryStream, cancellationToken);
        var bytes = memoryStream.ToArray();

        if (bytes.Length == 0)
        {
            return new ParsedDocument(string.Empty);
        }

        var (encoding, preambleLength) = DetectEncoding(bytes);

        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);

        text = text.Replace("\r\n", "\n");

        return new ParsedDocument(text);
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: false), 3);
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: false), 2);
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: false), 2);
            }
        }

        return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false), 0);
    }
}