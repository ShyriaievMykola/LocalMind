using LocalMind.TestUtilities;

namespace LocalMind.Core.Tests.TestUtilities;

public class FakeTokenCounterTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("слово", 1)]
    [InlineData("Docker, compose і bridge-мережа!", 5)]
    [InlineData("v2 and 10.0", 4)]
    public void CountTokens_CountsWords(string text, int expected)
    {
        Assert.Equal(expected, new FakeTokenCounter().CountTokens(text));
    }

    [Fact]
    public void CountTokens_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FakeTokenCounter().CountTokens(null!));
    }
}
