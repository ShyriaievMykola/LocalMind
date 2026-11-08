using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Ingestion.Chunking;
using LocalMind.Core.Ingestion.Parsers;
using LocalMind.Core.Models;
using LocalMind.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalMind.Core.Tests.Ingestion;

public sealed class FolderIndexerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly InMemoryDocumentRepository _repository = new();
    private readonly FakeEmbedder _embedder = new();
    private readonly FolderIndexer _indexer;

    public FolderIndexerTests()
    {
        Directory.CreateDirectory(_folder);

        IDocumentParser[] parsers = [new PlainTextParser(), new MarkdownParser(), new PdfParser(), new DocxParser()];
        var chunker = new FixedSizeChunker(Options.Create(new ChunkingOptions()), new FakeTokenCounter());
        _indexer = new FolderIndexer(
            new ParserRegistry(parsers),
            chunker,
            _embedder,
            _repository,
            NullLogger<FolderIndexer>.Instance);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Index_StoresDocumentsChunksAndVectors()
    {
        Write("a.txt", "кава зранку");
        Write(Path.Combine("sub", "b.md"), "# Заголовок\n\nчай увечері");

        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(2, result.Indexed);
        Assert.Equal(2, result.Chunks);
        Assert.Equal(2, (await _repository.GetDocumentsAsync()).Count);
        Assert.Equal(2, _repository.AllChunks.Count);
        Assert.Equal(2, await CountVectorsAsync());
        Assert.Equal(Path.GetFullPath(_folder), Assert.Single(await _repository.GetSourceFoldersAsync()));
    }

    [Fact]
    public async Task Index_SkipsUnsupportedFiles()
    {
        Write("image.png", "not text");

        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(new FolderIndexingResult(), result);
        Assert.Empty(await _repository.GetDocumentsAsync());
    }

    [Fact]
    public async Task Index_BrokenFile_DoesNotStopOthersAndIsMarkedAsError()
    {
        Write("broken.pdf", "NOT_A_PDF");
        Write("good.txt", "кава");

        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Indexed);
        var broken = await _repository.GetDocumentByPathAsync(PathOf("broken.pdf"));
        Assert.Equal(DocumentStatus.Error, broken!.Status);
        Assert.False(string.IsNullOrWhiteSpace(broken.Error));
    }

    [Fact]
    public async Task Index_EmptyFile_GetsEmptyStatus()
    {
        Write("empty.txt", string.Empty);

        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(1, result.WithoutText);
        Assert.Equal(DocumentStatus.Empty, (await _repository.GetDocumentByPathAsync(PathOf("empty.txt")))!.Status);
    }

    [Fact]
    public async Task Index_TextOnlyWhitespace_GetsNoTextStatus()
    {
        Write("blank.txt", "   \n  ");

        await _indexer.IndexAsync(_folder);

        Assert.Equal(DocumentStatus.NoText, (await _repository.GetDocumentByPathAsync(PathOf("blank.txt")))!.Status);
    }

    [Fact]
    public async Task Index_SecondRun_SkipsUnchangedFiles()
    {
        Write("a.txt", "кава");
        await _indexer.IndexAsync(_folder);
        var callsAfterFirstRun = _embedder.Calls.Count;

        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(1, result.Unchanged);
        Assert.Equal(0, result.Indexed);
        Assert.Equal(callsAfterFirstRun, _embedder.Calls.Count);
    }

    [Fact]
    public async Task Index_ChangedFile_ReplacesOldChunks()
    {
        Write("a.txt", "старий текст");
        await _indexer.IndexAsync(_folder);

        Write("a.txt", "новий текст");
        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(1, result.Indexed);
        Assert.Equal("новий текст", Assert.Single(_repository.AllChunks).Text);
        Assert.Equal(1, await CountVectorsAsync());
    }

    [Fact]
    public async Task Index_DeletedFile_IsRemovedFromStorage()
    {
        Write("a.txt", "кава");
        Write("b.txt", "чай");
        await _indexer.IndexAsync(_folder);

        File.Delete(PathOf("a.txt"));
        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(1, result.Removed);
        Assert.Equal(PathOf("b.txt"), Assert.Single(await _repository.GetDocumentsAsync()).Path);
    }

    [Fact]
    public async Task Index_ReportsProgressUntilCompleted()
    {
        Write("a.txt", "кава");
        Write("b.txt", "чай");
        var reports = new List<IndexingProgress>();

        await _indexer.IndexAsync(_folder, new SynchronousProgress(reports.Add));

        Assert.True(reports[^1].IsCompleted);
        Assert.Equal(2, reports[^1].Total);
        Assert.Equal(PathOf("a.txt"), reports[0].CurrentFile);
    }

    [Fact]
    public async Task Index_MissingFolder_Throws()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _indexer.IndexAsync(PathOf("missing")));
    }

    [Fact]
    public async Task Index_Canceled_Throws()
    {
        Write("a.txt", "кава");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _indexer.IndexAsync(_folder, cancellationToken: cancellation.Token));
    }

    private string PathOf(string name) => Path.Combine(_folder, name);

    private void Write(string name, string text)
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private async Task<int> CountVectorsAsync()
    {
        var count = 0;
        await foreach (var _ in _repository.GetVectorsAsync(_embedder.ModelName))
        {
            count++;
        }

        return count;
    }

    private sealed class SynchronousProgress(Action<IndexingProgress> report) : IProgress<IndexingProgress>
    {
        public void Report(IndexingProgress value) => report(value);
    }
}
