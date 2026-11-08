using LocalMind.Core.Models;
using LocalMind.Core.Storage;
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
