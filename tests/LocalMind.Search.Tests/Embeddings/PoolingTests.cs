using LocalMind.Search.Embeddings;

namespace LocalMind.Search.Tests.Embeddings;

public class PoolingTests
{
    [Fact]
    public void MeanPool_AveragesTokensAndNormalizes()
    {
        var result = Pooling.MeanPool([2f, 0f, 4f, 8f], [1L, 1L], 2);

        Assert.Equal(0.6f, result[0], 0.0001f);
        Assert.Equal(0.8f, result[1], 0.0001f);
    }

    [Fact]
    public void MeanPool_IgnoresPadding()
    {
        var withPadding = Pooling.MeanPool([3f, 4f, 100f, -100f], [1L, 0L], 2);
        var withoutPadding = Pooling.MeanPool([3f, 4f], [1L], 2);

        Assert.Equal(withoutPadding, withPadding);
    }

    [Fact]
    public void MeanPool_OnlyPadding_ReturnsZeroVector()
    {
        Assert.Equal([0f, 0f], Pooling.MeanPool([1f, 1f], [0L], 2));
    }

    [Fact]
    public void MeanPool_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => Pooling.MeanPool([1f, 2f, 3f], [1L], 2));
    }

    [Fact]
    public void NormalizeL2_MakesUnitLength()
    {
        float[] vector = [3f, 4f];

        Pooling.NormalizeL2(vector);

        Assert.Equal(0.6f, vector[0], 0.0001f);
        Assert.Equal(0.8f, vector[1], 0.0001f);
    }

    [Fact]
    public void NormalizeL2_ZeroVector_StaysZero()
    {
        float[] vector = [0f, 0f];

        Pooling.NormalizeL2(vector);

        Assert.Equal([0f, 0f], vector);
    }
}
