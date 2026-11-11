using LocalMind.Core.Models;
using LocalMind.Core.Storage;
using LocalMind.Core.Storage.Entities;
using LocalMind.TestUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Core.Tests.Storage;

public sealed class DocumentRepositoryTests : IAsyncLifetime, IDisposable
{
    private const string Model = "test-model";

    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private TestContextFactory _factory = null!;
    private DocumentRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LocalMindDbContext>().UseSqlite(_connection).Options;
        _factory = new TestContextFactory(options);
        await using var context = _factory.CreateDbContext();
        await context.Database.MigrateAsync();
        _repository = new DocumentRepository(_factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Migration_MatchesModel()
    {
        await using var context = _factory.CreateDbContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Save_ThenRead_ReturnsSameData()
    {
        var document = TestData.Document("/notes/a.md");
        var chunks = TestData.Chunks(document.Id, "перший", "другий");

        var replaced = await _repository.SaveAsync(document, chunks, Vectors(chunks.Count), Model);

        Assert.Empty(replaced);
        Assert.Equal(document, await _repository.GetDocumentAsync(document.Id));
        Assert.Equal(document, await _repository.GetDocumentByPathAsync("/notes/a.md"));
        var stored = await _repository.GetChunksAsync([.. chunks.Select(c => c.Id)]);
        Assert.Equal(chunks.OrderBy(c => c.Id), stored.OrderBy(c => c.Id));
    }

    [Fact]
    public async Task GetDocuments_ByIds_ReturnsOnlyExistingRequested()
    {
        var wanted = TestData.Document("/notes/a.md");
        var other = TestData.Document("/notes/b.md");
        await _repository.SaveAsync(wanted, [], [], Model);
        await _repository.SaveAsync(other, [], [], Model);

        var documents = await _repository.GetDocumentsAsync([wanted.Id, Guid.NewGuid()]);

        Assert.Equal(wanted, Assert.Single(documents));
    }

    [Fact]
    public async Task Save_ThenGetVectors_ReturnsSameVectors()
    {
        var document = TestData.Document();
        var chunks = TestData.Chunks(document.Id, "текст");
        float[] vector = [0.25f, -1.5f, 3f];

        await _repository.SaveAsync(document, chunks, [vector], Model);

        var stored = await CollectVectorsAsync(Model);
        Assert.Equal(chunks[0].Id, Assert.Single(stored).ChunkId);
        Assert.Equal(vector, stored[0].Vector.ToArray());
    }

    [Fact]
    public async Task GetVectors_OtherModel_ReturnsNothing()
    {
        var document = TestData.Document();
        var chunks = TestData.Chunks(document.Id, "текст");
        await _repository.SaveAsync(document, chunks, Vectors(1), Model);

        Assert.Empty(await CollectVectorsAsync("other-model"));
    }

    [Fact]
    public async Task Save_SamePath_ReplacesOldChunksAndReturnsTheirIds()
    {
        var oldDocument = TestData.Document("/notes/a.md");
        var oldChunks = TestData.Chunks(oldDocument.Id, "старий 1", "старий 2");
        await _repository.SaveAsync(oldDocument, oldChunks, Vectors(2), Model);

        var newDocument = TestData.Document("/notes/a.md", hashSeed: 1);
        var newChunks = TestData.Chunks(newDocument.Id, "новий");
        var replaced = await _repository.SaveAsync(newDocument, newChunks, Vectors(1), Model);

        Assert.Equal(oldChunks.Select(c => c.Id).Order(), replaced.Order());
        Assert.Null(await _repository.GetDocumentAsync(oldDocument.Id));
        Assert.Empty(await _repository.GetChunksAsync([.. oldChunks.Select(c => c.Id)]));
        Assert.Equal(newChunks[0].Id, Assert.Single(await CollectVectorsAsync(Model)).ChunkId);
    }

    [Fact]
    public async Task Save_FailureInsideTransaction_KeepsOldVersion()
    {
        var document = TestData.Document("/notes/a.md");
        var chunks = TestData.Chunks(document.Id, "старий");
        await _repository.SaveAsync(document, chunks, Vectors(1), Model);

        // Дублікат id фрагмента ламає вставку вже після видалення старої версії.
        var newDocument = TestData.Document("/notes/a.md", hashSeed: 1);
        var duplicate = new Chunk(Guid.NewGuid(), newDocument.Id, 0, "новий", 0);
        var broken = new Chunk(duplicate.Id, newDocument.Id, 1, "новий", 0);
        await Assert.ThrowsAnyAsync<Exception>(
            () => _repository.SaveAsync(newDocument, [duplicate, broken], Vectors(2), Model));

        Assert.Equal(document, await _repository.GetDocumentByPathAsync("/notes/a.md"));
        Assert.Single(await _repository.GetChunksAsync([chunks[0].Id]));
    }

    [Fact]
    public async Task Save_WithoutChunks_StoresDocumentOnly()
    {
        var document = TestData.Document(status: DocumentStatus.Empty);

        await _repository.SaveAsync(document, [], [], Model);

        Assert.Equal(document, await _repository.GetDocumentAsync(document.Id));
        Assert.Empty(await CollectVectorsAsync(Model));
    }

    [Fact]
    public async Task Save_VectorCountMismatch_Throws()
    {
        var document = TestData.Document();
        var chunks = TestData.Chunks(document.Id, "текст");

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.SaveAsync(document, chunks, [], Model));
    }

    [Fact]
    public async Task Save_ChunkOfOtherDocument_Throws()
    {
        var document = TestData.Document();
        var chunks = TestData.Chunks(Guid.NewGuid(), "текст");

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.SaveAsync(document, chunks, Vectors(1), Model));
    }

    [Fact]
    public async Task Save_SeveralDocuments_ReusesModel()
    {
        foreach (var path in new[] { "/notes/a.md", "/notes/b.md" })
        {
            var document = TestData.Document(path);
            await _repository.SaveAsync(document, TestData.Chunks(document.Id, "текст"), Vectors(1), Model);
        }

        await using var context = _factory.CreateDbContext();
        Assert.Equal(1, await context.EmbeddingModels.CountAsync());
        Assert.Equal(2, (await CollectVectorsAsync(Model)).Count);
    }

    [Fact]
    public async Task Delete_RemovesChunksAndVectors()
    {
        var document = TestData.Document();
        var chunks = TestData.Chunks(document.Id, "1", "2");
        await _repository.SaveAsync(document, chunks, Vectors(2), Model);

        var removed = await _repository.DeleteAsync(document.Id);

        Assert.Equal(chunks.Select(c => c.Id).Order(), removed.Order());
        Assert.Empty(await _repository.GetDocumentsAsync());
        Assert.Empty(await CollectVectorsAsync(Model));
    }

    [Fact]
    public async Task Delete_MissingDocument_ReturnsEmpty()
    {
        Assert.Empty(await _repository.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveSourceFolder_OtherCase_FollowsOsRule()
    {
        await _repository.AddSourceFolderAsync("/Нотатки");
        var document = TestData.Document("/Нотатки/Кава.md");
        await _repository.SaveAsync(document, TestData.Chunks(document.Id, "кава"), Vectors(1), Model);

        var removed = await _repository.RemoveSourceFolderAsync("/нотатки");

        if (FilePaths.IgnoreCase)
        {
            Assert.Single(removed);
            Assert.Empty(await _repository.GetSourceFoldersAsync());
            Assert.Empty(await _repository.GetDocumentsAsync());
        }
        else
        {
            Assert.Empty(removed);
            Assert.Equal("/Нотатки", Assert.Single(await _repository.GetSourceFoldersAsync()));
            Assert.Single(await _repository.GetDocumentsAsync());
        }
    }

    [Fact]
    public async Task AddSourceFolder_OtherCase_FollowsOsRule()
    {
        await _repository.AddSourceFolderAsync("/Нотатки");
        await _repository.AddSourceFolderAsync("/НОТАТКИ");

        Assert.Equal(FilePaths.IgnoreCase ? 1 : 2, (await _repository.GetSourceFoldersAsync()).Count);
    }

    [Fact]
    public async Task DocumentPath_OtherCase_FollowsOsRule()
    {
        var document = TestData.Document("/Нотатки/Кава.md");
        await _repository.SaveAsync(document, [], [], Model);

        var found = await _repository.GetDocumentByPathAsync("/нотатки/КАВА.md");
        var other = TestData.Document("/нотатки/кава.md");
        var replaced = await _repository.SaveAsync(other, [], [], Model);

        if (FilePaths.IgnoreCase)
        {
            Assert.Equal(document, found);
            Assert.Equal(other, Assert.Single(await _repository.GetDocumentsAsync()));
        }
        else
        {
            Assert.Null(found);
            Assert.Empty(replaced);
            Assert.Equal(2, (await _repository.GetDocumentsAsync()).Count);
        }
    }

    [Fact]
    public async Task DocumentPath_OutsideBmp_FoundAndReplaced()
    {
        var path = "/нотатки/📝 план 𝄞.md";
        var first = TestData.Document(path);
        await _repository.SaveAsync(first, [], [], Model);

        Assert.Equal(first, await _repository.GetDocumentByPathAsync(path));

        var second = TestData.Document(path);
        await _repository.SaveAsync(second, [], [], Model);

        Assert.Equal(second, Assert.Single(await _repository.GetDocumentsAsync()));
    }

    [Fact]
    public async Task AddSourceFolder_Twice_KeepsOne()
    {
        await _repository.AddSourceFolderAsync("/notes/");
        await _repository.AddSourceFolderAsync("/notes");

        Assert.Equal("/notes", Assert.Single(await _repository.GetSourceFoldersAsync()));
    }

    [Theory]
    [InlineData("/")]
    [InlineData(@"C:\")]
    public async Task AddSourceFolder_Root_KeepsSeparator(string root)
    {
        await _repository.AddSourceFolderAsync(root);

        Assert.Equal(root, Assert.Single(await _repository.GetSourceFoldersAsync()));
    }

    [Theory]
    [InlineData("/", "/notes/a.md")]
    [InlineData(@"C:\", @"C:\notes\a.md")]
    public async Task RemoveSourceFolder_Root_RemovesDocumentsInsideIt(string root, string filePath)
    {
        await _repository.AddSourceFolderAsync(root);
        var document = TestData.Document(filePath);
        var chunks = TestData.Chunks(document.Id, "1");
        await _repository.SaveAsync(document, chunks, Vectors(1), Model);

        var removed = await _repository.RemoveSourceFolderAsync(root);

        Assert.Equal(chunks[0].Id, Assert.Single(removed));
        Assert.Empty(await _repository.GetSourceFoldersAsync());
        Assert.Empty(await _repository.GetDocumentsAsync());
    }

    [Fact]
    public async Task RemoveSourceFolder_RemovesOnlyItsDocuments()
    {
        await _repository.AddSourceFolderAsync("/notes");
        var inside = TestData.Document("/notes/sub/a.md");
        var insideChunks = TestData.Chunks(inside.Id, "1");
        var similarName = TestData.Document("/notes-old/b.md");
        await _repository.SaveAsync(inside, insideChunks, Vectors(1), Model);
        await _repository.SaveAsync(similarName, TestData.Chunks(similarName.Id, "2"), Vectors(1), Model);

        var removed = await _repository.RemoveSourceFolderAsync("/notes/");

        Assert.Equal(insideChunks[0].Id, Assert.Single(removed));
        Assert.Empty(await _repository.GetSourceFoldersAsync());
        Assert.Equal(similarName, Assert.Single(await _repository.GetDocumentsAsync()));
    }

    [Fact]
    public async Task DeleteDocumentRow_CascadesToChunksAndVectors()
    {
        var document = TestData.Document();
        await _repository.SaveAsync(document, TestData.Chunks(document.Id, "1", "2"), Vectors(2), Model);

        await using var context = _factory.CreateDbContext();
        await context.Database.ExecuteSqlAsync($"DELETE FROM Documents WHERE Id = {document.Id}");

        Assert.Equal(0, await context.Chunks.CountAsync());
        Assert.Equal(0, await context.Embeddings.CountAsync());
        Assert.Equal(1, await context.EmbeddingModels.CountAsync());
    }

    [Fact]
    public async Task DeleteModelWithVectors_IsRestricted()
    {
        var document = TestData.Document();
        await _repository.SaveAsync(document, TestData.Chunks(document.Id, "1"), Vectors(1), Model);

        await using var context = _factory.CreateDbContext();

        await Assert.ThrowsAsync<SqliteException>(
            () => context.Database.ExecuteSqlAsync($"DELETE FROM EmbeddingModels WHERE Name = {Model}"));
        Assert.Single(await CollectVectorsAsync(Model));
    }

    [Fact]
    public async Task InsertDuplicatePath_ViolatesUniqueIndex()
    {
        await _repository.SaveAsync(TestData.Document("/notes/a.md"), [], [], Model);

        await using var context = _factory.CreateDbContext();
        context.Documents.Add(new DocumentEntity { Id = Guid.NewGuid(), Path = "/notes/a.md", ContentHash = TestData.Hash(1) });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task InsertChunkOfMissingDocument_ViolatesForeignKey()
    {
        await using var context = _factory.CreateDbContext();
        context.Chunks.Add(new ChunkEntity { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Text = "сирота" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_Reindex_LeavesNoOrphanRows()
    {
        var path = "/notes/a.md";
        for (var version = 0; version < 3; version++)
        {
            var document = TestData.Document(path, hashSeed: version);
            await _repository.SaveAsync(document, TestData.Chunks(document.Id, "1", "2", "3"), Vectors(3), Model);
        }

        await using var context = _factory.CreateDbContext();
        Assert.Equal(1, await context.Documents.CountAsync());
        Assert.Equal(3, await context.Chunks.CountAsync());
        Assert.Equal(3, await context.Embeddings.CountAsync());
    }

    [Fact]
    public async Task Save_SameIdNewPath_ReplacesDocument()
    {
        var id = Guid.NewGuid();
        var old = TestData.Document("/notes/old.md", id: id);
        var oldChunks = TestData.Chunks(id, "старий");
        await _repository.SaveAsync(old, oldChunks, Vectors(1), Model);

        var moved = TestData.Document("/notes/new.md", id: id);
        var replaced = await _repository.SaveAsync(moved, TestData.Chunks(id, "новий"), Vectors(1), Model);

        Assert.Equal(oldChunks[0].Id, Assert.Single(replaced));
        Assert.Equal(moved, Assert.Single(await _repository.GetDocumentsAsync()));
        Assert.Null(await _repository.GetDocumentByPathAsync("/notes/old.md"));
    }

    [Fact]
    public async Task Save_Cancelled_KeepsOldVersion()
    {
        var document = TestData.Document("/notes/a.md");
        await _repository.SaveAsync(document, TestData.Chunks(document.Id, "старий"), Vectors(1), Model);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var newDocument = TestData.Document("/notes/a.md", hashSeed: 1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _repository.SaveAsync(newDocument, TestData.Chunks(newDocument.Id, "новий"), Vectors(1), Model, cts.Token));

        Assert.Equal(document, await _repository.GetDocumentByPathAsync("/notes/a.md"));
    }

    [Fact]
    public async Task Save_OtherModel_KeepsVectorsSeparate()
    {
        var first = TestData.Document("/notes/a.md");
        await _repository.SaveAsync(first, TestData.Chunks(first.Id, "1"), Vectors(1), Model);
        var second = TestData.Document("/notes/b.md");
        await _repository.SaveAsync(second, TestData.Chunks(second.Id, "2"), Vectors(1), "other-model");

        Assert.Single(await CollectVectorsAsync(Model));
        Assert.Single(await CollectVectorsAsync("other-model"));
    }

    [Theory]
    [InlineData("/notes/b.md")]
    [InlineData("/notes/a.md/")]
    public async Task GetDocumentByPath_OtherPath_ReturnsNull(string path)
    {
        await _repository.SaveAsync(TestData.Document("/notes/a.md"), [], [], Model);

        Assert.Null(await _repository.GetDocumentByPathAsync(path));
    }

    [Fact]
    public async Task CyrillicPath_RoundTrips()
    {
        var document = TestData.Document("/нотатки/Подорож до Львова.md");
        await _repository.AddSourceFolderAsync("/нотатки/");
        await _repository.SaveAsync(document, TestData.Chunks(document.Id, "квитки"), Vectors(1), Model);

        Assert.Equal("/нотатки", Assert.Single(await _repository.GetSourceFoldersAsync()));
        Assert.Equal(document, await _repository.GetDocumentByPathAsync("/нотатки/Подорож до Львова.md"));
        Assert.Single(await _repository.RemoveSourceFolderAsync("/нотатки"));
    }

    [Fact]
    public async Task RemoveSourceFolder_WindowsPaths_RemovesNestedOnly()
    {
        await _repository.AddSourceFolderAsync(@"C:\notes\");
        var nested = TestData.Document(@"C:\notes\sub\a.md");
        var similar = TestData.Document(@"C:\notes2\b.md");
        await _repository.SaveAsync(nested, TestData.Chunks(nested.Id, "1", "2"), Vectors(2), Model);
        await _repository.SaveAsync(similar, TestData.Chunks(similar.Id, "3"), Vectors(1), Model);

        var removed = await _repository.RemoveSourceFolderAsync(@"C:\notes");

        Assert.Equal(2, removed.Count);
        Assert.Equal(similar, Assert.Single(await _repository.GetDocumentsAsync()));
    }

    [Fact]
    public async Task RemoveSourceFolder_Unknown_KeepsEverything()
    {
        await _repository.AddSourceFolderAsync("/notes");
        await _repository.SaveAsync(TestData.Document("/notes/a.md"), [], [], Model);

        Assert.Empty(await _repository.RemoveSourceFolderAsync("/other"));
        Assert.Single(await _repository.GetSourceFoldersAsync());
        Assert.Single(await _repository.GetDocumentsAsync());
    }

    [Fact]
    public async Task Save_ErrorDocument_KeepsErrorText()
    {
        var document = TestData.Document(status: DocumentStatus.Error);

        await _repository.SaveAsync(document, [], [], Model);

        var stored = await _repository.GetDocumentAsync(document.Id);
        Assert.Equal(DocumentStatus.Error, stored!.Status);
        Assert.Equal(document.Error, stored.Error);
    }

    [Fact]
    public async Task GetVectors_Cancelled_Throws()
    {
        var document = TestData.Document();
        await _repository.SaveAsync(document, TestData.Chunks(document.Id, "1"), Vectors(1), Model);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _repository.GetVectorsAsync(Model, cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task InvalidArguments_Throw()
    {
        var document = TestData.Document();

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.AddSourceFolderAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.RemoveSourceFolderAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetDocumentByPathAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetDocumentsAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetChunksAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.SaveAsync(document, [], [], " "));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in _repository.GetVectorsAsync(""))
            {
            }
        });
    }

    private static List<ReadOnlyMemory<float>> Vectors(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new ReadOnlyMemory<float>([i, 1f]))];

    private async Task<List<ChunkVector>> CollectVectorsAsync(string model)
    {
        var vectors = new List<ChunkVector>();
        await foreach (var vector in _repository.GetVectorsAsync(model))
        {
            vectors.Add(vector);
        }

        return vectors;
    }

    private sealed class TestContextFactory(DbContextOptions<LocalMindDbContext> options)
        : IDbContextFactory<LocalMindDbContext>
    {
        public LocalMindDbContext CreateDbContext() => new(options);
    }
}
