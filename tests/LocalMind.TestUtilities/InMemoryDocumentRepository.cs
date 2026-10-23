using System.Runtime.CompilerServices;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;

namespace LocalMind.TestUtilities;

/// <summary>
/// Сховище в пам'яті з тією самою поведінкою, що описана в <see cref="IDocumentRepository"/>:
/// заміна документа за шляхом, видалення разом із фрагментами, вектори окремо для кожної моделі.
/// </summary>
public sealed class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly Lock _lock = new();
    private readonly List<string> _folders = [];
    private readonly Dictionary<Guid, Document> _documents = [];
    private readonly Dictionary<Guid, List<Chunk>> _chunksByDocument = [];
    private readonly Dictionary<(Guid ChunkId, string Model), ReadOnlyMemory<float>> _vectors = [];

    /// <summary>Усі збережені фрагменти — для фейкового keyword-індексу і перевірок у тестах.</summary>
    public IReadOnlyList<Chunk> AllChunks
    {
        get
        {
            lock (_lock)
            {
                return [.. _chunksByDocument.Values.SelectMany(c => c)];
            }
        }
    }

    public Task<IReadOnlyList<string>> GetSourceFoldersAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<string>>([.. _folders]);
        }
    }

    public Task AddSourceFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var folder = NormalizeFolder(path);

        lock (_lock)
        {
            if (!_folders.Contains(folder, StringComparer.Ordinal))
            {
                _folders.Add(folder);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> RemoveSourceFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        lock (_lock)
        {
            _folders.Remove(NormalizeFolder(path));

            var removed = new List<Guid>();
            foreach (var document in _documents.Values.Where(d => IsInsideFolder(d.Path, path)).ToList())
            {
                removed.AddRange(RemoveDocument(document.Id));
            }

            return Task.FromResult<IReadOnlyList<Guid>>(removed);
        }
    }

    /// <summary>Шлях папки без роздільника в кінці: <c>/notes/</c> і <c>/notes</c> — одна папка.</summary>
    private static string NormalizeFolder(string path) => path.TrimEnd('/', '\\');

    /// <summary>Чи лежить файл у папці; приймає обидва роздільники, щоб тести не залежали від ОС.</summary>
    private static bool IsInsideFolder(string filePath, string folderPath)
    {
        var folder = NormalizeFolder(folderPath);
        return filePath.Length > folder.Length
            && filePath.StartsWith(folder, StringComparison.Ordinal)
            && filePath[folder.Length] is '/' or '\\';
    }

    public Task<IReadOnlyList<Document>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<Document>>([.. _documents.Values]);
        }
    }

    public Task<Document?> GetDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_documents.GetValueOrDefault(id));
        }
    }

    public Task<Document?> GetDocumentByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(FindByPath(path));
        }
    }

    public Task<IReadOnlyList<Chunk>> GetChunksAsync(
        IReadOnlyCollection<Guid> chunkIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);

        lock (_lock)
        {
            var wanted = chunkIds.ToHashSet();
            return Task.FromResult<IReadOnlyList<Chunk>>(
                [.. _chunksByDocument.Values.SelectMany(c => c).Where(c => wanted.Contains(c.Id))]);
        }
    }

    public async IAsyncEnumerable<ChunkVector> GetVectorsAsync(
        string modelName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        List<ChunkVector> snapshot;
        lock (_lock)
        {
            snapshot = [.. _vectors.Where(v => v.Key.Model == modelName).Select(v => new ChunkVector(v.Key.ChunkId, v.Value))];
        }

        foreach (var vector in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return vector;
            await Task.Yield();
        }
    }

    public Task<IReadOnlyList<Guid>> SaveAsync(
        Document document,
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> vectors,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        if (vectors.Count != chunks.Count)
        {
            throw new ArgumentException(
                $"Кількість векторів ({vectors.Count}) не збігається з кількістю фрагментів ({chunks.Count}).",
                nameof(vectors));
        }

        if (chunks.Any(c => c.DocumentId != document.Id))
        {
            throw new ArgumentException("Усі фрагменти мають належати документу, що зберігається.", nameof(chunks));
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var removed = new List<Guid>();
            if (FindByPath(document.Path) is { } existing)
            {
                removed.AddRange(RemoveDocument(existing.Id));
            }

            removed.AddRange(RemoveDocument(document.Id));

            _documents[document.Id] = document;
            _chunksByDocument[document.Id] = [.. chunks];
            for (var i = 0; i < chunks.Count; i++)
            {
                _vectors[(chunks[i].Id, modelName)] = vectors[i];
            }

            return Task.FromResult<IReadOnlyList<Guid>>(removed);
        }
    }

    public Task<IReadOnlyList<Guid>> DeleteAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(RemoveDocument(documentId));
        }
    }

    private Document? FindByPath(string path) =>
        _documents.Values.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.Ordinal));

    /// <summary>Видаляє документ з фрагментами і векторами; викликається під <see cref="_lock"/>.</summary>
    private IReadOnlyList<Guid> RemoveDocument(Guid documentId)
    {
        if (!_documents.Remove(documentId))
        {
            return [];
        }

        var chunkIds = _chunksByDocument.Remove(documentId, out var chunks) ? chunks.Select(c => c.Id).ToHashSet() : [];
        foreach (var key in _vectors.Keys.Where(k => chunkIds.Contains(k.ChunkId)).ToList())
        {
            _vectors.Remove(key);
        }

        return [.. chunkIds];
    }
}
