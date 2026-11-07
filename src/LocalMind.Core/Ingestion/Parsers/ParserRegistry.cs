using LocalMind.Core.Abstractions;

namespace LocalMind.Core.Ingestion.Parsers;

/// <summary>Обирає парсер документа за розширенням файла.</summary>
public sealed class ParserRegistry
{
    private readonly Dictionary<string, IDocumentParser> _parsers = new(StringComparer.OrdinalIgnoreCase);

    public ParserRegistry(IEnumerable<IDocumentParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);

        foreach (var parser in parsers)
        {
            ArgumentNullException.ThrowIfNull(parser);

            foreach (var extension in parser.SupportedExtensions)
            {
                if (!_parsers.TryAdd(extension, parser))
                {
                    throw new InvalidOperationException($"Для розширення '{extension}' зареєстровано більше одного парсера.");
                }

            }
        }
    }

    /// <summary>Знаходить парсер для файла; для непідтримуваного розширення повертає <see langword="false"/>.</summary>
    /// <param name="filePath">Шлях до файла.</param>
    /// <param name="parser">Знайдений парсер або <see langword="null"/>.</param>
    public bool TryGetParser(string filePath, out IDocumentParser? parser)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        return _parsers.TryGetValue(Path.GetExtension(filePath), out parser);
    }
}