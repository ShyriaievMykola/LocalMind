using LocalMind.Core.Models;

namespace LocalMind.Core.Tests.Models;

public class SearchResultTests
{
    private static readonly Chunk SampleChunk = new(Guid.NewGuid(), Guid.NewGuid(), 0, "текст фрагмента", 0);

    [Fact]
    public void Constructor_ValidArguments_SetsProperties()
    {
        var result = new SearchResult(SampleChunk, "/notes/devops.md", 0.87);

        Assert.Same(SampleChunk, result.Chunk);
        Assert.Equal("/notes/devops.md", result.DocumentPath);
        Assert.Equal(0.87, result.Score);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(12.3)]
    public void Constructor_AnyFiniteScore_IsAllowed(double score)
    {
        Assert.Equal(score, new SearchResult(SampleChunk, "/a.md", score).Score);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Constructor_NonFiniteScore_Throws(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchResult(SampleChunk, "/a.md", score));
    }

    [Fact]
    public void Constructor_NullChunk_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SearchResult(null!, "/a.md", 1.0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankPath_Throws(string path)
    {
        Assert.Throws<ArgumentException>(() => new SearchResult(SampleChunk, path, 1.0));
    }
}
