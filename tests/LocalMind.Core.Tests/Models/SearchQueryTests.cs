using LocalMind.Core.Models;

namespace LocalMind.Core.Tests.Models;

public class SearchQueryTests
{
    [Fact]
    public void Constructor_TextOnly_UsesHybridModeAndDefaultTopK()
    {
        var query = new SearchQuery("docker networking");

        Assert.Equal("docker networking", query.Text);
        Assert.Equal(SearchMode.Hybrid, query.Mode);
        Assert.Equal(SearchQuery.DefaultTopK, query.TopK);
    }

    [Fact]
    public void Constructor_AllArguments_SetsProperties()
    {
        var query = new SearchQuery("CS0246", SearchMode.Keyword, 3);

        Assert.Equal(SearchMode.Keyword, query.Mode);
        Assert.Equal(3, query.TopK);
    }

    [Fact]
    public void Constructor_TextWithSurroundingSpaces_IsTrimmed()
    {
        Assert.Equal("як налаштувати ssh", new SearchQuery("  як налаштувати ssh \n").Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Constructor_BlankText_Throws(string text)
    {
        Assert.Throws<ArgumentException>(() => new SearchQuery(text));
    }

    [Fact]
    public void Constructor_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SearchQuery(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_NonPositiveTopK_Throws(int topK)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchQuery("запит", SearchMode.Hybrid, topK));
    }

    [Fact]
    public void Constructor_UndefinedMode_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchQuery("запит", (SearchMode)99));
    }
}
