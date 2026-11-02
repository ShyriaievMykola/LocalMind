using LocalMind.Core.Ingestion.Parsers;

namespace LocalMind.Core.Tests.Ingestion;

public class ParserTests
{
    private readonly PlainTextParser _parser = new();
    private readonly string _testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    private FileStream OpenFixture(string fileName)
    {
        var filePath = Path.Combine(_testDataPath, fileName);
        return File.OpenRead(filePath);
    }

    [Fact]
    public async Task ParseAsync_Utf8WithoutBom_ReturnsText()
    {
        using var stream = OpenFixture("utf8.txt");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal("Hello World! UTF-8 without BOM.", result.Text);
    }

    [Fact]
    public async Task ParseAsync_Utf8WithBom_ReturnsTextWithoutBom()
    {
        using var stream = OpenFixture("utf8-bom.txt");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal("Hello World! UTF-8 with BOM.", result.Text);
    }

    [Fact]
    public async Task ParseAsync_Utf16LittleEndianWithBom_ReturnsText()
    {
        using var stream = OpenFixture("utf16-le.txt");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal("Hello World! UTF-16 LE.", result.Text);
    }

    [Fact]
    public async Task ParseAsync_Utf16BigEndianWithBom_ReturnsText()
    {
        using var stream = OpenFixture("utf16-be.txt");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal("Hello World! UTF-16 BE.", result.Text);
    }

    [Fact]
    public async Task ParseAsync_CyrillicAndLatin_PreservesText()
    {
        using var stream = OpenFixture("cyrillic.txt");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Contains("Привіт, світ!", result.Text);
        Assert.Contains("Hello world!", result.Text);
    }

    [Fact]
    public async Task ParseAsync_CrlfLineEndings_NormalizesToLf()
    {
        using var stream = OpenFixture("crlf.txt");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.DoesNotContain("\r\n", result.Text);
        Assert.Equal("Line 1\nLine 2\nLine 3", result.Text);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsEmptyDocument()
    {
        using var stream = OpenFixture("empty.txt");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Text);
        Assert.False(result.HasText);
    }

    [Fact]
    public async Task ParseAsync_InvalidUtf8Bytes_ReplacesWithReplacementCharacter()
    {
        using var stream = OpenFixture("invalid-utf8.bin");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Contains('\uFFFD', result.Text);
    }

    [Fact]
    public async Task ParseAsync_DoesNotCloseStream()
    {
        using var stream = OpenFixture("utf8.txt");

        _ = await _parser.ParseAsync(stream);

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ParseAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using var stream = OpenFixture("utf8.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _parser.ParseAsync(stream, cts.Token));
    }
}