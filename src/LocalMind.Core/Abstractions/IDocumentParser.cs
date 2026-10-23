using LocalMind.Core.Models;

namespace LocalMind.Core.Abstractions;

/// <summary>Витягує чистий текст із файла певного формату (TXT, Markdown, PDF, DOCX).</summary>
public interface IDocumentParser
{
    /// <summary>
    /// Розширення файлів, які підтримує парсер: у нижньому регістрі, з крапкою (<c>".md"</c>).
    /// </summary>
    IReadOnlySet<string> SupportedExtensions { get; }

    /// <summary>Розбирає вміст файла.</summary>
    /// <param name="content">Потік із вмістом файла; парсер його не закриває.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>
    /// Текст документа. Для порожнього файла або файла без текстового шару —
    /// <see cref="ParsedDocument"/> з порожнім текстом, а не виняток.
    /// </returns>
    /// <exception cref="Errors.DocumentParseException">Файл пошкоджений, зашифрований або не відповідає формату.</exception>
    /// <exception cref="OperationCanceledException">Операцію скасовано.</exception>
    Task<ParsedDocument> ParseAsync(Stream content, CancellationToken cancellationToken = default);
}
