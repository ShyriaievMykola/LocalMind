using LocalMind.Core.Ingestion.Parsers;

namespace LocalMind.Core.Tests.Ingestion;

public class MarkdownParserTests
{
    private readonly MarkdownParser _parser = new();
    private readonly string _testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    private FileStream OpenFixture(string fileName)
    {
        var filePath = Path.Combine(_testDataPath, fileName);
        return File.OpenRead(filePath);
    }

    [Fact]
    public async Task ParseAsync_ValidMarkdown_ExtractsTextWithoutFormatting()
    {
        using var stream = OpenFixture("sample.md");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Contains("Header 1", result.Text);
        Assert.Contains("This is a text paragraph with a Google link and an image: Alt Text.", result.Text);
        Assert.Contains("Features", result.Text);
        Assert.Contains("Item 1", result.Text);
        Assert.Contains("Nested item", result.Text);
        Assert.Contains("Header A", result.Text);
        Assert.Contains("Header B", result.Text);
        Assert.Contains("Value 1", result.Text);
        Assert.Contains("Value 2", result.Text);
        Assert.DoesNotContain("|", result.Text);
    }

    [Fact]
    public async Task ParseAsync_YamlFrontMatter_IgnoresMetadata()
    {
        using var stream = OpenFixture("front-matter.md");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.DoesNotContain("Secret Document", result.Text);
        Assert.DoesNotContain("author: Pavel", result.Text);
        Assert.DoesNotContain("---", result.Text);

        Assert.Contains("Main Title", result.Text);
        Assert.Contains("Document content goes here.", result.Text);
    }

    [Fact]
    public async Task ParseAsync_FirstLevelHeading_SetsTitleProperty()
    {
        using var stream = OpenFixture("front-matter.md");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal("Main Title", result.Title);
    }

    [Fact]
    public async Task ParseAsync_CodeBlocksAndLinks_PreservesCodeAndAltText()
    {
        using var stream = OpenFixture("sample.md");

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Contains("var x = 10;", result.Text);
        Assert.Contains("Console.WriteLine(x);", result.Text);
        Assert.Contains("Google", result.Text);
        Assert.DoesNotContain("https://google.com", result.Text);
        Assert.Contains("Alt Text", result.Text);
    }

    [Fact]
    public async Task ParseAsync_EmptyMarkdownStream_ReturnsEmptyParsedDocument()
    {
        using var stream = new MemoryStream();

        var result = await _parser.ParseAsync(stream);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Text);
        Assert.Null(result.Title);
        Assert.False(result.HasText);
    }
}