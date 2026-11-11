using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion.Parsers;
namespace LocalMind.Core.Tests.Ingestion;

public class PdfParserTests
{
    private readonly PdfParser _parser = new();
    private readonly string _testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    private FileStream OpenFixture(string fileName)
    {
        var filePath = Path.Combine(_testDataPath, fileName);
        return File.OpenRead(filePath);
    }

    [Fact]
    public async Task ParseAsync_ValidTextPdf_ExtractsTextAndTitle()
    {
        using var stream = OpenFixture("sample.pdf");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.True(result.HasText);
        Assert.Contains("Hello World", result.Text);
        Assert.Equal("Test PDF Document", result.Title);
        Assert.Contains("Перевірка", result.Text);
    }
    [Fact]
    public async Task ParseAsync_MultiplePages_SeparatesPagesWithDoubleNewline()
    {
        using var stream = OpenFixture("multipage.pdf");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal("Hello World! Перевірка кирилиці у PDF. ello World! Перевірка кирилиці у PDF.\n\nPage 2 Content", result.Text);
    }

    [Fact]
    public async Task ParseAsync_ScannedPdfWithoutTextLayer_ReturnsEmptyDocumentWithoutException()
    {
        using var stream = OpenFixture("scanned.pdf");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Text);
        Assert.False(result.HasText);
    }

    [Fact]
    public async Task ParseAsync_EncryptedPdf_ThrowsDocumentParseException()
    {
        using var stream = OpenFixture("encrypted.pdf");

        var exception = await Assert.ThrowsAsync<DocumentParseException>(() => _parser.ParseAsync(stream));

        Assert.Contains("зашифровано", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseAsync_CorruptedPdf_ThrowsDocumentParseException()
    {
        using var stream = OpenFixture("corrupted.pdf");

        var exception = await Assert.ThrowsAsync<DocumentParseException>(() => _parser.ParseAsync(stream));

        Assert.Contains("пошкоджений", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsEmptyParsedDocument()
    {
        using var stream = new MemoryStream();

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Text);
        Assert.Null(result.Title);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ParseAsync_FileLargerThanLimit_ThrowsDocumentParseException(bool seekable)
    {
        var parser = new PdfParser(maxFileSizeBytes: 16);
        using var fixture = OpenFixture("sample.pdf");
        using Stream stream = seekable ? fixture : new NonSeekableStream(fixture);

        var exception = await Assert.ThrowsAsync<DocumentParseException>(() => parser.ParseAsync(stream));

        Assert.Contains("завеликий", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseAsync_EndlessStream_StopsAtLimit()
    {
        var parser = new PdfParser(maxFileSizeBytes: 1024 * 1024);
        using var stream = new NonSeekableStream(new EndlessStream());

        await Assert.ThrowsAsync<DocumentParseException>(() => parser.ParseAsync(stream));
    }

    /// <summary>Нескінченний потік нулів: без ліміту копіювання з'їло б усю пам'ять.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => count;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
