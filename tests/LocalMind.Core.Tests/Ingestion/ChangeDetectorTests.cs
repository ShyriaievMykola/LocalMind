using System.Security.Cryptography;
using System.Text;
using LocalMind.Core.Ingestion.Watching;
using LocalMind.Core.Models;
using LocalMind.TestUtilities;

namespace LocalMind.Core.Tests.Ingestion;

public sealed class ChangeDetectorTests : IDisposable
{
    private const string Model = "test-model";
    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-tests-").FullName;
    private readonly InMemoryDocumentRepository _repository = new();
    private readonly ChangeDetector _detector;

    public ChangeDetectorTests() => _detector = new ChangeDetector(_repository);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task DetectAsync_FileNotInRepository_ReturnsNew()
    {
        var path = await WriteFileAsync("note.md", "нова нотатка");

        var change = await _detector.DetectAsync(path);

        Assert.Equal(FileChangeKind.New, change.Kind);
        Assert.Equal(Sha256("нова нотатка"), change.ContentHash);
        Assert.Null(change.StoredDocument);
        Assert.True(change.NeedsIndexing);
    }

    [Fact]
    public async Task DetectAsync_SameContentAsStored_ReturnsUnchanged()
    {
        var path = await WriteFileAsync("note.md", "текст");
        var stored = await StoreAsync(path, Sha256("текст"));

        var change = await _detector.DetectAsync(path);

        Assert.Equal(FileChangeKind.Unchanged, change.Kind);
        Assert.Equal(stored, change.StoredDocument);
        Assert.False(change.NeedsIndexing);
    }

    [Fact]
    public async Task DetectAsync_SameContentButStoredVectorsAreOfAnotherModel_ReturnsModified()
    {
        var path = await WriteFileAsync("note.md", "текст");
        await StoreWithVectorAsync(path, Sha256("текст"), Model);

        var sameModel = await new ChangeDetector(_repository, new FakeEmbedder(modelName: Model)).DetectAsync(path);
        var otherModel = await new ChangeDetector(_repository, new FakeEmbedder(modelName: "other-model")).DetectAsync(path);

        Assert.Equal(FileChangeKind.Unchanged, sameModel.Kind);
        Assert.Equal(FileChangeKind.Modified, otherModel.Kind);
        Assert.True(otherModel.NeedsIndexing);
    }

    [Fact]
    public async Task DetectAsync_DocumentWithoutTextDoesNotNeedVectorsOfAnyModel()
    {
        var path = await WriteFileAsync("empty.md", "текст");
        var document = new Document(Guid.NewGuid(), path, Sha256("текст"), DocumentStatus.NoText, TestData.Now);
        await _repository.SaveAsync(document, [], [], Model);

        var change = await new ChangeDetector(_repository, new FakeEmbedder(modelName: "other-model")).DetectAsync(path);

        Assert.Equal(FileChangeKind.Unchanged, change.Kind);
    }

    [Fact]
    public async Task DetectAsync_ContentChanged_ReturnsModified()
    {
        var path = await WriteFileAsync("note.md", "стара версія");
        var stored = await StoreAsync(path, Sha256("стара версія"));
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("нова версія"));

        var change = await _detector.DetectAsync(path);

        Assert.Equal(FileChangeKind.Modified, change.Kind);
        Assert.Equal(Sha256("нова версія"), change.ContentHash);
        Assert.Equal(stored, change.StoredDocument);
        Assert.True(change.NeedsIndexing);
    }

    [Fact]
    public async Task DetectAsync_FileDeletedFromDisk_ReturnsDeleted()
    {
        var path = await WriteFileAsync("note.md", "текст");
        var stored = await StoreAsync(path, Sha256("текст"));
        File.Delete(path);

        var change = await _detector.DetectAsync(path);

        Assert.Equal(FileChangeKind.Deleted, change.Kind);
        Assert.Null(change.ContentHash);
        Assert.Equal(stored, change.StoredDocument);
        Assert.False(change.NeedsIndexing);
    }

    [Fact]
    public async Task DetectAsync_FolderDeleted_ReturnsDeleted()
    {
        var path = Path.Combine(_folder, "removed-folder", "note.md");
        var stored = await StoreAsync(path, Sha256("текст"));

        var change = await _detector.DetectAsync(path);

        Assert.Equal(FileChangeKind.Deleted, change.Kind);
        Assert.Equal(stored, change.StoredDocument);
    }

    [Fact]
    public async Task DetectAsync_MissingFileNotInRepository_ReturnsDeletedWithoutDocument()
    {
        var path = Path.Combine(_folder, "missing.md");

        var change = await _detector.DetectAsync(path);

        Assert.Equal(FileChangeKind.Deleted, change.Kind);
        Assert.Null(change.StoredDocument);
    }

    [Fact]
    public async Task DetectAsync_SameContentDifferentFileNames_ReturnsSameHash()
    {
        var first = await WriteFileAsync("a.txt", "однаковий вміст");
        var second = await WriteFileAsync("b.md", "однаковий вміст");

        var firstChange = await _detector.DetectAsync(first);
        var secondChange = await _detector.DetectAsync(second);

        Assert.NotNull(firstChange.ContentHash);
        Assert.Equal(firstChange.ContentHash, secondChange.ContentHash);
    }

    [Fact]
    public async Task DetectAsync_EmptyFile_ReturnsHashOfEmptyContent()
    {
        var path = await WriteFileAsync("empty.txt", string.Empty);

        var change = await _detector.DetectAsync(path);

        Assert.Equal(FileChangeKind.New, change.Kind);
        Assert.Equal(EmptySha256, change.ContentHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DetectAsync_EmptyPath_ThrowsArgumentException(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _detector.DetectAsync(path));
    }

    [Fact]
    public async Task DetectAsync_NullPath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _detector.DetectAsync(null!));
    }

    [Fact]
    public async Task DetectAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        var path = await WriteFileAsync("note.md", "текст");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _detector.DetectAsync(path, cts.Token));
    }

    [Fact]
    public async Task ComputeHashAsync_KnownInput_MatchesReferenceSha256()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"));

        var hash = await ChangeDetector.ComputeHashAsync(stream);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public async Task ComputeHashAsync_NullStream_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ChangeDetector.ComputeHashAsync(null!));
    }

    private async Task<string> WriteFileAsync(string name, string content)
    {
        var path = Path.Combine(_folder, name);
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private async Task<Document> StoreAsync(string path, string hash)
    {
        var document = new Document(Guid.NewGuid(), path, hash, DocumentStatus.Indexed, TestData.Now);
        await _repository.SaveAsync(document, [], [], Model);
        return document;
    }

    private async Task StoreWithVectorAsync(string path, string hash, string model)
    {
        var document = new Document(Guid.NewGuid(), path, hash, DocumentStatus.Indexed, TestData.Now);
        var chunk = new Chunk(Guid.NewGuid(), document.Id, 0, "текст", 0);
        float[] vector = [1f, 0f];
        await _repository.SaveAsync(document, [chunk], [vector], model);
    }

    private static string Sha256(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}