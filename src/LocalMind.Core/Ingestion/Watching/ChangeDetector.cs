using System.Security.Cryptography;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;

namespace LocalMind.Core.Ingestion.Watching;

/// <summary>
/// Визначає, чи треба переіндексувати файл: рахує SHA-256 вмісту і порівнює з хешем,
/// збереженим у сховищі. Незмінені файли не переіндексовуються (AC-09). Проіндексований файл без векторів
/// поточної моделі (модель змінили) вважається зміненим: інакше семантичний пошук його не бачив би.
/// </summary>
public sealed class ChangeDetector(IDocumentRepository repository, IEmbedder? embedder = null)
{
    /// <summary>Розмір буфера читання: файл читається частинами, а не цілком у пам'ять.</summary>
    private const int BufferSize = 81_920;

    /// <summary>Порівнює файл на диску з версією у сховищі.</summary>
    /// <param name="path">Повний шлях до файла — у тому ж вигляді, у якому його зберігає сховище.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>
    /// <see cref="FileChangeKind.New"/> — файла немає у сховищі;
    /// <see cref="FileChangeKind.Modified"/> — хеш відрізняється від збереженого;
    /// <see cref="FileChangeKind.Unchanged"/> — хеш збігається;
    /// <see cref="FileChangeKind.Deleted"/> — файла немає на диску.
    /// </returns>
    /// <exception cref="IOException">Файл не вдалося прочитати (наприклад, його заблокувала інша програма).</exception>
    /// <exception cref="UnauthorizedAccessException">Немає доступу до файла.</exception>
    public async Task<FileChange> DetectAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var stored = await repository.GetDocumentByPathAsync(path, cancellationToken);
        var hash = await TryComputeFileHashAsync(path, cancellationToken);

        if (hash is null)
        {
            return new FileChange(path, FileChangeKind.Deleted, ContentHash: null, stored);
        }

        if (stored is null)
        {
            return new FileChange(path, FileChangeKind.New, hash, StoredDocument: null);
        }

        var sameContent = string.Equals(stored.ContentHash, hash, StringComparison.Ordinal);
        if (sameContent
            && embedder is not null
            && stored.Status == DocumentStatus.Indexed
            && !await repository.HasVectorsAsync(stored.Id, embedder.ModelName, cancellationToken))
        {
            sameContent = false;
        }

        var kind = sameContent ? FileChangeKind.Unchanged : FileChangeKind.Modified;

        return new FileChange(path, kind, hash, stored);
    }

    /// <summary>
    /// SHA-256 вмісту потоку в нижньому регістрі — 64 шістнадцяткові символи, як <c>Document.ContentHash</c>.
    /// Потік читається частинами, тому розмір файла не впливає на використання пам'яті.
    /// </summary>
    /// <param name="content">Потік з вмістом; не закривається.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    public static async Task<string> ComputeHashAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var hash = await SHA256.HashDataAsync(content, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Хеш файла або <see langword="null"/>, якщо файла (чи його папки) вже немає.</summary>
    private static async Task<string?> TryComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return await ComputeHashAsync(stream, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}