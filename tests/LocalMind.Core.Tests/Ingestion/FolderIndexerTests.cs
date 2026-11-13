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

    private FolderIndexer IndexerWithLimits(IngestionOptions limits) =>
        new(
            new ParserRegistry([new PlainTextParser(), new MarkdownParser(), new PdfParser(), new DocxParser()]),
            new FixedSizeChunker(Options.Create(new ChunkingOptions()), new FakeTokenCounter()),
            _embedder,
            _repository,
            NullLogger<FolderIndexer>.Instance,
            Options.Create(limits));

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
    public async Task Index_PdfLargerThanLimit_IsMarkedAsErrorAndOthersAreIndexed()
    {
        var indexer = new FolderIndexer(
            new ParserRegistry([new PlainTextParser(), new PdfParser(maxFileSizeBytes: 16)]),
            new FixedSizeChunker(Options.Create(new ChunkingOptions()), new FakeTokenCounter()),
            _embedder,
            _repository,
            NullLogger<FolderIndexer>.Instance);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "sample.pdf"), PathOf("big.pdf"));
        Write("good.txt", "кава");

        var result = await indexer.IndexAsync(_folder);

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Indexed);
        var big = await _repository.GetDocumentByPathAsync(PathOf("big.pdf"));
        Assert.Equal(DocumentStatus.Error, big!.Status);
        Assert.Contains("завеликий", big.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_FileLargerThanSharedLimit_IsRejectedBeforeParsingAndNotRetried()
    {
        var indexer = IndexerWithLimits(new IngestionOptions { MaxFileSizeBytes = 64 });
        Write("big.txt", new string('я', 100));
        Write("good.txt", "кава");

        var first = await indexer.IndexAsync(_folder);
        var second = await indexer.IndexAsync(_folder);

        Assert.Equal(1, first.Failed);
        Assert.Equal(1, first.Indexed);
        var big = await _repository.GetDocumentByPathAsync(PathOf("big.txt"));
        Assert.Equal(DocumentStatus.Error, big!.Status);
        Assert.Contains("завеликий", big.Error, StringComparison.Ordinal);
        Assert.Equal(1, second.Failed);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(0, second.Indexed);
    }

    [Fact]
    public async Task Index_ModelNotFound_StoresChunksWithoutVectorsAndTriesTheModelOnce()
    {
        Write("a.txt", "кава зранку");
        Write("b.txt", "чай увечері");
        var missingModel = new MissingModelEmbedder();
        var indexer = new FolderIndexer(
            new ParserRegistry([new PlainTextParser()]),
            new FixedSizeChunker(Options.Create(new ChunkingOptions()), new FakeTokenCounter()),
            missingModel,
            _repository,
            NullLogger<FolderIndexer>.Instance);

        var result = await indexer.IndexAsync(_folder);

        Assert.Equal(2, result.Indexed);
        Assert.Equal(2, result.WithoutVectors);
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, missingModel.Calls);
        Assert.Equal(2, _repository.AllChunks.Count);
        Assert.Equal(0, await CountVectorsAsync());
        Assert.All(await _repository.GetDocumentsAsync(), d => Assert.Equal(DocumentStatus.Indexed, d.Status));
    }

    [Fact]
    public async Task Index_ModelBroken_StillFailsInsteadOfSilentlyDegrading()
    {
        Write("a.txt", "кава зранку");
        var indexer = new FolderIndexer(
            new ParserRegistry([new PlainTextParser()]),
            new FixedSizeChunker(Options.Create(new ChunkingOptions()), new FakeTokenCounter()),
            new MissingModelEmbedder(new Core.Errors.LocalMindException("Файл моделі пошкоджений.")),
            _repository,
            NullLogger<FolderIndexer>.Instance);

        var exception = await Assert.ThrowsAsync<Core.Errors.LocalMindException>(() => indexer.IndexAsync(_folder));

        Assert.Contains("пошкоджений", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_TextWithPdfExtension_IsMarkedAsErrorBySignature()
    {
        Write("fake.pdf", "Це звичайний текст у файлі з розширенням .pdf.");

        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(1, result.Failed);
        var document = await _repository.GetDocumentByPathAsync(PathOf("fake.pdf"));
        Assert.Equal(DocumentStatus.Error, document!.Status);
        Assert.Contains("%PDF", document.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_ZipBombNamedDocx_IsMarkedAsError()
    {
        var path = PathOf("bomb.docx");
        using (var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var entry = archive.CreateEntry("word/document.xml", System.IO.Compression.CompressionLevel.SmallestSize).Open();
            entry.Write(new byte[8 * 1024 * 1024]);
        }

        var indexer = IndexerWithLimits(new IngestionOptions { MaxArchiveUncompressedBytes = 1024 * 1024 });

        var result = await indexer.IndexAsync(_folder);

        Assert.Equal(1, result.Failed);
        var document = await _repository.GetDocumentByPathAsync(path);
        Assert.Contains("zip-бомб", document!.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_UnchangedBrokenFile_IsStillCountedAsFailed()
    {
        Write("broken.pdf", "NOT_A_PDF");
        await _indexer.IndexAsync(_folder);

        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Unchanged);
    }

    [Fact]
    public async Task Index_SameFolderInOtherCase_KeepsOneDocument()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return; // На Linux це інша папка.
        }

        Write("a.txt", "старий текст");
        await _indexer.IndexAsync(_folder);
        var otherCase = Path.Combine(Path.GetDirectoryName(_folder)!, Path.GetFileName(_folder).ToUpperInvariant());

        var unchanged = await _indexer.IndexAsync(otherCase);
        Write("a.txt", "новий текст");
        var changed = await _indexer.IndexAsync(otherCase);

        Assert.Equal(1, unchanged.Unchanged);
        Assert.Equal(0, unchanged.Removed);
        Assert.Equal(1, changed.Indexed);
        Assert.Equal(PathOf("a.txt"), Assert.Single(await _repository.GetDocumentsAsync()).Path);
        Assert.Equal("новий текст", Assert.Single(_repository.AllChunks).Text);
    }

    [Fact]
    public async Task Index_FilesDifferingOnlyInCase_AreSeparateOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // На Windows і macOS це один файл.
        }

        Write("note.txt", "кава");
        Write("NOTE.txt", "чай");

        var result = await _indexer.IndexAsync(_folder);

        Assert.Equal(2, result.Indexed);
        Assert.Equal(2, (await _repository.GetDocumentsAsync()).Count);
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

    /// <summary>Embedder без файлів моделі (або зі зламаною моделлю, якщо передати інший виняток).</summary>
    private sealed class MissingModelEmbedder(Exception? failure = null) : IEmbedder
    {
        public int Calls { get; private set; }

        public string ModelName => "missing-model";

        public int Dimensions => 8;

        public int MaxTokens => 512;

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
            IReadOnlyList<string> texts,
            EmbeddingPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw failure ?? new Core.Errors.ModelNotFoundException("Файл моделі не знайдено.");
        }
    }
}
