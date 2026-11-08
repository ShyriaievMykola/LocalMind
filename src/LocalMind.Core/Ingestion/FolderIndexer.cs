using System.Security.Cryptography;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion.Parsers;
using LocalMind.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalMind.Core.Ingestion;

/// <summary>
/// Послідовно індексує папку: файл → парсер → фрагменти → вектори → сховище.
/// Незмінені файли пропускаються; зіпсований файл не зупиняє індексацію решти.
/// </summary>
public sealed partial class FolderIndexer(
    ParserRegistry parsers,
    IChunker chunker,
    IEmbedder embedder,
    IDocumentRepository repository,
    ILogger<FolderIndexer> logger)
{
    /// <summary>Індексує всі підтримувані файли папки та її підпапок.</summary>
    /// <param name="folder">Шлях до папки.</param>
    /// <param name="progress">Отримує стан після кожного файла.</param>
    /// <param name="cancellationToken">Скасування; вже оброблені файли лишаються збереженими.</param>
    /// <exception cref="DirectoryNotFoundException">Папки не існує.</exception>
    public async Task<FolderIndexingResult> IndexAsync(
        string folder,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Папку не знайдено: {root}");
        }

        await repository.AddSourceFolderAsync(root, cancellationToken);

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => parsers.TryGetParser(path, out _))
            .Order(StringComparer.Ordinal)
            .ToList();

        var result = new FolderIndexingResult();
        for (var i = 0; i < files.Count; i++)
        {
            progress?.Report(new IndexingProgress(i, files.Count, result.Failed, files[i]));
            result = await IndexFileAsync(files[i], result, cancellationToken);
        }

        result = result with { Removed = await RemoveMissingFilesAsync(root, files, cancellationToken) };
        progress?.Report(new IndexingProgress(files.Count, files.Count, result.Failed));
        return result;
    }

    private async Task<FolderIndexingResult> IndexFileAsync(
        string path,
        FolderIndexingResult result,
        CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFileFailed(logger, path, exception.Message);
            return result with { Failed = result.Failed + 1 };
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var existing = await repository.GetDocumentByPathAsync(path, cancellationToken);
        if (existing is not null && existing.ContentHash == hash)
        {
            return result with { Unchanged = result.Unchanged + 1 };
        }

        var id = Guid.NewGuid();
        ParsedDocument parsed;
        try
        {
            parsers.TryGetParser(path, out var parser);
            using var stream = new MemoryStream(content, writable: false);
            parsed = await parser!.ParseAsync(stream, cancellationToken);
        }
        catch (DocumentParseException exception)
        {
            LogFileFailed(logger, path, exception.Message);
            var failed = new Document(id, path, hash, DocumentStatus.Error, DateTimeOffset.UtcNow, exception.Message);
            await repository.SaveAsync(failed, [], [], embedder.ModelName, cancellationToken);
            return result with { Failed = result.Failed + 1 };
        }

        var chunks = parsed.HasText ? chunker.Split(id, parsed.Text) : [];
        if (chunks.Count == 0)
        {
            var status = content.Length == 0 ? DocumentStatus.Empty : DocumentStatus.NoText;
            var empty = new Document(id, path, hash, status, DateTimeOffset.UtcNow);
            await repository.SaveAsync(empty, [], [], embedder.ModelName, cancellationToken);
            return result with { WithoutText = result.WithoutText + 1 };
        }

        var vectors = await embedder.EmbedAsync([.. chunks.Select(c => c.Text)], EmbeddingPurpose.Passage, cancellationToken);
        var document = new Document(id, path, hash, DocumentStatus.Indexed, DateTimeOffset.UtcNow);
        await repository.SaveAsync(document, chunks, vectors, embedder.ModelName, cancellationToken);
        return result with { Indexed = result.Indexed + 1, Chunks = result.Chunks + chunks.Count };
    }

    /// <summary>Прибирає зі сховища документи цієї папки, файлів яких уже немає.</summary>
    private async Task<int> RemoveMissingFilesAsync(
        string root,
        List<string> files,
        CancellationToken cancellationToken)
    {
        var existing = files.ToHashSet(StringComparer.Ordinal);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var removed = 0;

        foreach (var document in await repository.GetDocumentsAsync(cancellationToken))
        {
            if (document.Path.StartsWith(prefix, StringComparison.Ordinal) && !existing.Contains(document.Path))
            {
                await repository.DeleteAsync(document.Id, cancellationToken);
                removed++;
            }
        }

        return removed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Файл пропущено: {Path}. {Reason}")]
    private static partial void LogFileFailed(ILogger logger, string path, string reason);
}

/// <summary>Підсумок індексації папки.</summary>
/// <param name="Indexed">Скільки файлів проіндексовано.</param>
/// <param name="Chunks">Скільки фрагментів збережено.</param>
/// <param name="Unchanged">Скільки файлів пропущено, бо вони не змінились.</param>
/// <param name="WithoutText">Скільки файлів порожні або без тексту (наприклад, скани).</param>
/// <param name="Failed">Скільки файлів не вдалося обробити.</param>
/// <param name="Removed">Скільки документів прибрано, бо їхніх файлів уже немає.</param>
public sealed record FolderIndexingResult(
    int Indexed = 0,
    int Chunks = 0,
    int Unchanged = 0,
    int WithoutText = 0,
    int Failed = 0,
    int Removed = 0);
