using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using LocalMind.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Core.Storage;

/// <summary>Сховище бібліотеки в SQLite. Кожна операція працює зі своїм контекстом, тому клас потокобезпечний.</summary>
public sealed class DocumentRepository(IDbContextFactory<LocalMindDbContext> contextFactory) : IDocumentRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetSourceFoldersAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SourceFolders.AsNoTracking().Select(f => f.Path).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddSourceFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var folder = NormalizeFolder(path);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.SourceFolders.AnyAsync(f => f.Path == folder, cancellationToken))
        {
            context.SourceFolders.Add(new SourceFolderEntity { Path = folder });
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> RemoveSourceFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var folder = NormalizeFolder(path);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.SourceFolders.Where(f => f.Path == folder).ExecuteDeleteAsync(cancellationToken);

        var documents = await context.Documents.AsNoTracking()
            .Select(d => new { d.Id, d.Path })
            .ToListAsync(cancellationToken);
        var documentIds = documents.Where(d => IsInsideFolder(d.Path, folder)).Select(d => d.Id).ToList();
        var removed = await RemoveDocumentsAsync(context, documentIds, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Documents.AsNoTracking().ToListAsync(cancellationToken);
        return [.. entities.Select(ToModel)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> GetDocumentsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Documents.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(cancellationToken);
        return [.. entities.Select(ToModel)];
    }
    
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> GetChunkCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Chunks.AsNoTracking()
            .GroupBy(c => c.DocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Count, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Document?> GetDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    /// <inheritdoc />
    public async Task<Document?> GetDocumentByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Path == path, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Chunk>> GetChunksAsync(
        IReadOnlyCollection<Guid> chunkIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Chunks.AsNoTracking()
            .Where(c => chunkIds.Contains(c.Id))
            .ToListAsync(cancellationToken);
        return [.. entities.Select(c => new Chunk(c.Id, c.DocumentId, c.Ordinal, c.Text, c.StartOffset))];
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChunkVector> GetVectorsAsync(
        string modelName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var embeddings = context.Embeddings.AsNoTracking()
            .Where(e => e.Model!.Name == modelName)
            .Select(e => new { e.ChunkId, e.Vector })
            .AsAsyncEnumerable();

        await foreach (var embedding in embeddings.WithCancellation(cancellationToken))
        {
            yield return new ChunkVector(embedding.ChunkId, ToFloats(embedding.Vector));
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> SaveAsync(
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

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var replacedIds = await context.Documents
            .Where(d => d.Path == document.Path || d.Id == document.Id)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        var removed = await RemoveDocumentsAsync(context, replacedIds, cancellationToken);

        var entity = new DocumentEntity
        {
            Id = document.Id,
            Path = document.Path,
            ContentHash = document.ContentHash,
            Status = document.Status,
            IndexedAt = document.IndexedAt,
            Error = document.Error,
        };

        if (chunks.Count > 0)
        {
            var model = await GetOrAddModelAsync(context, modelName, cancellationToken);
            for (var i = 0; i < chunks.Count; i++)
            {
                entity.Chunks.Add(new ChunkEntity
                {
                    Id = chunks[i].Id,
                    Ordinal = chunks[i].Ordinal,
                    Text = chunks[i].Text,
                    StartOffset = chunks[i].StartOffset,
                    Embeddings = [new EmbeddingEntity { ModelId = model.Id, Vector = ToBytes(vectors[i]) }],
                });
            }
        }

        context.Documents.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> DeleteAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var removed = await RemoveDocumentsAsync(context, [documentId], cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    /// <summary>Видаляє документи; фрагменти і вектори прибирає каскад у БД. Повертає id видалених фрагментів.</summary>
    private static async Task<IReadOnlyList<Guid>> RemoveDocumentsAsync(
        LocalMindDbContext context,
        List<Guid> documentIds,
        CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        var chunkIds = await context.Chunks
            .Where(c => documentIds.Contains(c.DocumentId))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        await context.Documents.Where(d => documentIds.Contains(d.Id)).ExecuteDeleteAsync(cancellationToken);
        return chunkIds;
    }

    private static async Task<EmbeddingModelEntity> GetOrAddModelAsync(
        LocalMindDbContext context,
        string modelName,
        CancellationToken cancellationToken)
    {
        var model = await context.EmbeddingModels.FirstOrDefaultAsync(m => m.Name == modelName, cancellationToken);
        if (model is null)
        {
            model = new EmbeddingModelEntity { Name = modelName };
            context.EmbeddingModels.Add(model);
            await context.SaveChangesAsync(cancellationToken);
        }

        return model;
    }

    /// <summary>
    /// Шлях папки без роздільника в кінці: <c>/notes/</c> і <c>/notes</c> — одна папка.
    /// Корінь (<c>/</c>, <c>C:\</c>) лишається без змін, бо без роздільника він означав би інший шлях.
    /// </summary>
    private static string NormalizeFolder(string path) => Path.TrimEndingDirectorySeparator(path);

    /// <summary>Чи лежить файл у папці; шлях кореня вже закінчується роздільником.</summary>
    private static bool IsInsideFolder(string filePath, string folder) =>
        filePath.Length > folder.Length
        && filePath.StartsWith(folder, StringComparison.Ordinal)
        && (folder[^1] is '/' or '\\' || filePath[folder.Length] is '/' or '\\');

    private static Document ToModel(DocumentEntity entity) =>
        new(entity.Id, entity.Path, entity.ContentHash, entity.Status, entity.IndexedAt, entity.Error);

    private static byte[] ToBytes(ReadOnlyMemory<float> vector) => MemoryMarshal.AsBytes(vector.Span).ToArray();

    private static float[] ToFloats(byte[] bytes) => MemoryMarshal.Cast<byte, float>(bytes).ToArray();
}
