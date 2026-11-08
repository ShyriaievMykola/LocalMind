using LocalMind.Search.Vectors;

namespace LocalMind.Search.Tests.Vectors;

public class VectorMathTests
{
    [Fact]
    public void DotProduct_ReturnsSumOfProducts()
    {
        Assert.Equal(32f, VectorMath.DotProduct([1f, 2f, 3f], [4f, 5f, 6f]));
    }

    [Fact]
    public void DotProduct_MatchesNaiveLoopOnLongVectors()
    {
        var random = new Random(42);
        var left = Enumerable.Range(0, 1001).Select(_ => random.NextSingle()).ToArray();
        var right = Enumerable.Range(0, 1001).Select(_ => random.NextSingle()).ToArray();

        var naive = 0f;
        for (var i = 0; i < left.Length; i++)
        {
            naive += left[i] * right[i];
        }

        Assert.Equal(naive, VectorMath.DotProduct(left, right), 0.01f);
    }

    [Fact]
    public void DotProduct_DifferentLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() => VectorMath.DotProduct([1f, 2f], [1f]));
    }
}
