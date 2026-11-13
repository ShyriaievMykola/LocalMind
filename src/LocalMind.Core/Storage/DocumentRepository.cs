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
        var folders = await context.SourceFolders.Select(f => f.Path).ToListAsync(cancellationToken);
        if (!folders.Any(f => FilePaths.AreSame(f, folder)))
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

        // Папку могли додати в іншому регістрі; папок небагато, тому порівнюємо в пам'яті за правилом ОС.
        var stored = (await context.SourceFolders.Select(f => f.Path).ToListAsync(cancellationToken))
            .Where(f => FilePaths.AreSame(f, folder))
            .ToList();
        await context.SourceFolders.Where(f => stored.Contains(f.Path)).ExecuteDeleteAsync(cancellationToken);

        var documents = await context.Documents.AsNoTracking()
            .Select(d => new { d.Id, d.Path })
            .ToListAsync(cancellationToken);
        var documentIds = documents.Where(d => FilePaths.IsSameOrInside(d.Path, folder)).Select(d => d.Id).ToList();
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
        var candidates = await SamePathQuery(context.Documents.AsNoTracking(), path).ToListAsync(cancellationToken);
        var entity = candidates.FirstOrDefault(d => d.Path == path) ?? candidates.FirstOrDefault(d => FilePaths.AreSame(d.Path, path));
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
    public async Task<IReadOnlyList<Chunk>> GetChunksByDocumentAsync(
        Guid documentId, int fromOrdinal, int toOrdinal, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromOrdinal);
        ArgumentOutOfRangeException.ThrowIfLessThan(toOrdinal, fromOrdinal);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Chunks.AsNoTracking()
            .Where(c => c.DocumentId == documentId && c.Ordinal >= fromOrdinal && c.Ordinal <= toOrdinal)
            .OrderBy(c => c.Ordinal)
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
    public async Task<IReadOnlySet<Guid>> GetDocumentIdsWithVectorsAsync(
        string modelName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await context.Embeddings.AsNoTracking()
            .Where(e => e.Model!.Name == modelName)
            .Select(e => e.Chunk!.DocumentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    /// <inheritdoc />
    public async Task<bool> HasVectorsAsync(Guid documentId, string modelName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Embeddings.AsNoTracking()
            .AnyAsync(e => e.Model!.Name == modelName && e.Chunk!.DocumentId == documentId, cancellationToken);
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

        if (vectors.Count != 0 && vectors.Count != chunks.Count)
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

        var replacedIds = (await SamePathQuery(context.Documents, document.Path)
                .Select(d => new { d.Id, d.Path })
                .ToListAsync(cancellationToken))
            .Where(d => FilePaths.AreSame(d.Path, document.Path))
            .Select(d => d.Id)
            .ToList();
        if (!replacedIds.Contains(document.Id) && await context.Documents.AnyAsync(d => d.Id == document.Id, cancellationToken))
        {
            replacedIds.Add(document.Id);
        }
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
            // Без векторів (модель не завантажена) фрагменти доступні лише keyword-пошуку.
            var model = vectors.Count > 0 ? await GetOrAddModelAsync(context, modelName, cancellationToken) : null;
            for (var i = 0; i < chunks.Count; i++)
            {
                entity.Chunks.Add(new ChunkEntity
                {
                    Id = chunks[i].Id,
                    Ordinal = chunks[i].Ordinal,
                    Text = chunks[i].Text,
                    StartOffset = chunks[i].StartOffset,
                    Embeddings = model is null
                        ? []
                        : [new EmbeddingEntity { ModelId = model.Id, Vector = ToBytes(vectors[i]) }],
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

    /// <summary>
    /// Кандидати на той самий шлях. SQLite порівнює рядки побайтово, а його <c>NOCASE</c> і <c>lower()</c> знають лише
    /// латиницю — кириличні шляхи так не зрівняти. Тому, коли регістр не важливий, відбираємо за довжиною
    /// (зміна регістру її не змінює), а точне порівняння робимо в пам'яті за <see cref="FilePaths"/>.
    /// SQLite <c>length()</c> рахує символи Unicode, а не UTF-16 одиниці, тому й тут рахуємо руни: інакше шлях
    /// з емодзі ніколи не знайшовся б.
    /// </summary>
    private static IQueryable<DocumentEntity> SamePathQuery(IQueryable<DocumentEntity> documents, string path)
    {
        if (!FilePaths.IgnoreCase)
        {
            return documents.Where(d => d.Path == path);
        }

        var length = path.EnumerateRunes().Count();
        return documents.Where(d => d.Path.Length == length);
    }

    private static Document ToModel(DocumentEntity entity) =>
        new(entity.Id, entity.Path, entity.ContentHash, entity.Status, entity.IndexedAt, entity.Error);

    private static byte[] ToBytes(ReadOnlyMemory<float> vector) => MemoryMarshal.AsBytes(vector.Span).ToArray();

    private static float[] ToFloats(byte[] bytes) => MemoryMarshal.Cast<byte, float>(bytes).ToArray();
}
