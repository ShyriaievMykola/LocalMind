using LocalMind.Core.Models;

namespace LocalMind.Core.Tests.Models;

public class ParsedDocumentTests
{
    [Fact]
    public void Constructor_TextAndTitle_SetsProperties()
    {
        var parsed = new ParsedDocument("Вміст нотатки", "Docker networking");

        Assert.Equal("Вміст нотатки", parsed.Text);
        Assert.Equal("Docker networking", parsed.Title);
        Assert.True(parsed.HasText);
    }

    [Fact]
    public void Constructor_EmptyText_IsAllowedAndHasNoText()
    {
        var parsed = new ParsedDocument(string.Empty);

        Assert.Equal(string.Empty, parsed.Text);
        Assert.False(parsed.HasText);
    }

    [Fact]
    public void HasText_WhitespaceOnly_IsFalse()
    {
        Assert.False(new ParsedDocument("  \n\t  ").HasText);
    }

    [Fact]
    public void Constructor_TitleWithSpaces_IsTrimmed()
    {
        Assert.Equal("Заголовок", new ParsedDocument("текст", "  Заголовок  ").Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankTitle_BecomesNull(string? title)
    {
        Assert.Null(new ParsedDocument("текст", title).Title);
    }

    [Fact]
    public void Constructor_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ParsedDocument(null!));
    }
}
