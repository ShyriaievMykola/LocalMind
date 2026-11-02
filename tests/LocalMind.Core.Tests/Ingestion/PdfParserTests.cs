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
    public async Task ParseAsync_MultiPagePdf_SeparatesPagesWithDoubleNewline()
    {
        using var stream = OpenFixture("multipage.pdf");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Contains("\n\n", result.Text);
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
}