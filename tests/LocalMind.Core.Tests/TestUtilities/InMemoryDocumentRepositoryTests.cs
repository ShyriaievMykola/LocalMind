using LocalMind.Core.Models;
using LocalMind.TestUtilities;

namespace LocalMind.Core.Tests.TestUtilities;

public class InMemoryDocumentRepositoryTests
{
    private const string Model = "fake-model";

    private static ReadOnlyMemory<float>[] Vectors(int count) =>
        [.. Enumerable.Range(0, count).Select(i => (ReadOnlyMemory<float>)new float[] { i, 1 })];

    private static async Task<List<ChunkVector>> VectorsOf(InMemoryDocumentRepository repository, string model)
    {
        var result = new List<ChunkVector>();
        await foreach (var vector in repository.GetVectorsAsync(model))
        {
            result.Add(vector);
        }

        return result;
    }

    [Fact]
    public async Task GetDocuments_ByIds_ReturnsOnlyExistingRequested()
    {
        var repository = new InMemoryDocumentRepository();
        var wanted = TestData.Document("/notes/a.md");
        var other = TestData.Document("/notes/b.md");
        await repository.SaveAsync(wanted, [], [], Model);
        await repository.SaveAsync(other, [], [], Model);

        var documents = await repository.GetDocumentsAsync([wanted.Id, Guid.NewGuid()]);

        Assert.Equal(wanted, Assert.Single(documents));
    }

    [Fact]
    public async Task GetChunkCounts_ReturnsCountPerDocument_WithoutDocumentsWithoutChunks()
    {
        var repository = new InMemoryDocumentRepository();
        var first = TestData.Document("/notes/a.md");
        var empty = TestData.Document("/notes/empty.md", DocumentStatus.Empty);
        await repository.SaveAsync(first, TestData.Chunks(first.Id, "один", "два"), Vectors(2), Model);
        await repository.SaveAsync(empty, [], [], Model);

        var counts = await repository.GetChunkCountsAsync();

        Assert.Equal(2, counts[first.Id]);
        Assert.DoesNotContain(empty.Id, counts);
    }

    [Fact]
    public async Task Save_ThenDocumentChunksAndVectorsAreAvailable()
    {
        var repository = new InMemoryDocumentRepository();
        var document = TestData.Document("/notes/docker.md");
        var chunks = TestData.Chunks(document.Id, "перший фрагмент", "другий фрагмент");

        var replaced = await repository.SaveAsync(document, chunks, Vectors(2), Model);

        Assert.Empty(replaced);
        Assert.Equal(document, await repository.GetDocumentAsync(document.Id));
        Assert.Equal(document, await repository.GetDocumentByPathAsync("/notes/docker.md"));
        Assert.Equal([document], await repository.GetDocumentsAsync());
        Assert.Equal(2, (await repository.GetChunksAsync([chunks[0].Id, chunks[1].Id, Guid.NewGuid()])).Count);
        Assert.Equal(2, (await VectorsOf(repository, Model)).Count);
        Assert.Empty(await VectorsOf(repository, "other-model"));
    }

    [Fact]
    public async Task Save_SamePath_ReplacesOldDocumentAndReturnsOldChunkIds()
    {
        var repository = new InMemoryDocumentRepository();
        var old = TestData.Document("/notes/a.md", hashSeed: 1);
        var oldChunks = TestData.Chunks(old.Id, "стара версія");
        await repository.SaveAsync(old, oldChunks, Vectors(1), Model);

        var updated = TestData.Document("/notes/a.md", hashSeed: 2);
        var replaced = await repository.SaveAsync(updated, TestData.Chunks(updated.Id, "нова версія"), Vectors(1), Model);

        Assert.Equal([oldChunks[0].Id], replaced);
        Assert.Null(await repository.GetDocumentAsync(old.Id));
        Assert.Equal(updated, await repository.GetDocumentByPathAsync("/notes/a.md"));
        Assert.Single(await VectorsOf(repository, Model));
        Assert.Equal("нова версія", Assert.Single(repository.AllChunks).Text);
    }

    [Fact]
    public async Task Save_DocumentWithoutChunks_IsAllowed()
    {
        var repository = new InMemoryDocumentRepository();
        var document = TestData.Document(status: DocumentStatus.Error);

        await repository.SaveAsync(document, [], [], Model);

        Assert.Equal(DocumentStatus.Error, (await repository.GetDocumentAsync(document.Id))!.Status);
    }

    [Fact]
    public async Task Save_VectorCountMismatch_Throws()
    {
        var document = TestData.Document();

        await Assert.ThrowsAsync<ArgumentException>(
            () => new InMemoryDocumentRepository().SaveAsync(document, TestData.Chunks(document.Id, "a", "b"), Vectors(1), Model));
    }

    [Fact]
    public async Task Save_ChunkOfAnotherDocument_Throws()
    {
        var document = TestData.Document();

        await Assert.ThrowsAsync<ArgumentException>(
            () => new InMemoryDocumentRepository().SaveAsync(document, TestData.Chunks(Guid.NewGuid(), "a"), Vectors(1), Model));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Save_BlankModelName_Throws(string model)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => new InMemoryDocumentRepository().SaveAsync(TestData.Document(), [], [], model));
    }

    [Fact]
    public async Task Delete_ReturnsChunkIdsAndRemovesEverything()
    {
        var repository = new InMemoryDocumentRepository();
        var document = TestData.Document();
        var chunks = TestData.Chunks(document.Id, "a", "b");
        await repository.SaveAsync(document, chunks, Vectors(2), Model);

        var removed = await repository.DeleteAsync(document.Id);

        Assert.Equal(chunks.Select(c => c.Id).Order(), removed.Order());
        Assert.Empty(await repository.GetDocumentsAsync());
        Assert.Empty(repository.AllChunks);
        Assert.Empty(await VectorsOf(repository, Model));
    }

    [Fact]
    public async Task Delete_MissingDocument_ReturnsEmpty()
    {
        Assert.Empty(await new InMemoryDocumentRepository().DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AddSourceFolder_Twice_KeepsOneEntry()
    {
        var repository = new InMemoryDocumentRepository();

        await repository.AddSourceFolderAsync("/notes");
        await repository.AddSourceFolderAsync("/notes");

        Assert.Equal(["/notes"], await repository.GetSourceFoldersAsync());
    }

    [Theory]
    [InlineData("/notes")]
    [InlineData("/notes/")]
    public async Task RemoveSourceFolder_RemovesOnlyDocumentsInsideIt(string folder)
    {
        var repository = new InMemoryDocumentRepository();
        await repository.AddSourceFolderAsync("/notes");
        var inside = TestData.Document("/notes/sub/a.md");
        var insideBackslash = TestData.Document(@"/notes\b.md");
        var sibling = TestData.Document("/notes2/c.md");
        var insideChunks = TestData.Chunks(inside.Id, "a");
        await repository.SaveAsync(inside, insideChunks, Vectors(1), Model);
        await repository.SaveAsync(insideBackslash, [], [], Model);
        await repository.SaveAsync(sibling, TestData.Chunks(sibling.Id, "c"), Vectors(1), Model);

        var removed = await repository.RemoveSourceFolderAsync(folder);

        Assert.Equal([insideChunks[0].Id], removed);
        Assert.Equal([sibling], await repository.GetDocumentsAsync());
        Assert.Empty(await repository.GetSourceFoldersAsync());
    }

    [Fact]
    public async Task GetVectors_Cancelled_Throws()
    {
        var repository = new InMemoryDocumentRepository();
        var document = TestData.Document();
        await repository.SaveAsync(document, TestData.Chunks(document.Id, "a"), Vectors(1), Model);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in repository.GetVectorsAsync(Model, cts.Token))
            {
            }
        });
    }
}
