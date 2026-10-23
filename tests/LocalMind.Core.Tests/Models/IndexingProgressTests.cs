using LocalMind.Core.Models;

namespace LocalMind.Core.Tests.Models;

public class IndexingProgressTests
{
    [Fact]
    public void Constructor_ValidArguments_SetsProperties()
    {
        var progress = new IndexingProgress(3, 10, 1, "/notes/a.md");

        Assert.Equal(3, progress.Processed);
        Assert.Equal(10, progress.Total);
        Assert.Equal(1, progress.Failed);
        Assert.Equal("/notes/a.md", progress.CurrentFile);
    }

    [Fact]
    public void Fraction_PartiallyProcessed_IsRatio()
    {
        var progress = new IndexingProgress(1, 4);

        Assert.Equal(0.25, progress.Fraction);
        Assert.False(progress.IsCompleted);
    }

    [Fact]
    public void Fraction_AllProcessed_IsOneAndCompleted()
    {
        var progress = new IndexingProgress(4, 4);

        Assert.Equal(1.0, progress.Fraction);
        Assert.True(progress.IsCompleted);
    }

    [Fact]
    public void Fraction_EmptyQueue_IsOneAndCompleted()
    {
        var progress = new IndexingProgress(0, 0);

        Assert.Equal(1.0, progress.Fraction);
        Assert.True(progress.IsCompleted);
    }

    [Theory]
    [InlineData(-1, 5, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(6, 5, 0)]
    [InlineData(2, 5, -1)]
    [InlineData(2, 5, 3)]
    public void Constructor_InconsistentCounts_Throws(int processed, int total, int failed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexingProgress(processed, total, failed));
    }
}
