using LocalMind.Search.Evaluation;

namespace LocalMind.Search.Tests.Evaluation;

public class RankingMetricsTests
{
    // Релевантні: a (3) і b (1). Система віддала x, a, y, b, z — a на 2-му місці, b на 4-му.
    private static readonly string[] Ranked = ["x", "a", "y", "b", "z"];
    private static readonly Dictionary<string, int> Relevant = new() { ["a"] = 3, ["b"] = 1 };

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0.5)]
    [InlineData(3, 0.5)]
    [InlineData(4, 1)]
    [InlineData(10, 1)]
    public void RecallAtK_CountsRelevantFoundInTopK(int k, double expected)
    {
        Assert.Equal(expected, RankingMetrics.RecallAtK(Ranked, Relevant, k), precision: 10);
    }

    [Fact]
    public void ReciprocalRank_FirstRelevantAtRankTwo_IsHalf()
    {
        Assert.Equal(0.5, RankingMetrics.ReciprocalRank(Ranked, Relevant, 10), precision: 10);
    }

    [Fact]
    public void ReciprocalRank_FirstRelevantBeyondK_IsZero()
    {
        Assert.Equal(0, RankingMetrics.ReciprocalRank(Ranked, Relevant, 1));
    }

    [Fact]
    public void NdcgAtK_HandComputedExample()
    {
        // DCG = 3/log2(3) + 1/log2(5) = 1.8928 + 0.4307 = 2.3235;
        // ідеал: 3/log2(2) + 1/log2(3) = 3 + 0.6309 = 3.6309; nDCG = 0.6399.
        Assert.Equal(0.6399, RankingMetrics.NdcgAtK(Ranked, Relevant, 5), precision: 3);
    }

    [Fact]
    public void NdcgAtK_IdealOrder_IsOne()
    {
        Assert.Equal(1, RankingMetrics.NdcgAtK(["a", "b", "x"], Relevant, 10), precision: 10);
    }

    [Fact]
    public void NdcgAtK_BetterDocumentFirst_ScoresHigher()
    {
        var better = RankingMetrics.NdcgAtK(["a", "b"], Relevant, 10);
        var worse = RankingMetrics.NdcgAtK(["b", "a"], Relevant, 10);

        Assert.True(better > worse);
    }

    [Fact]
    public void AllMetrics_NothingRelevantFound_AreZero()
    {
        string[] ranked = ["x", "y", "z"];

        Assert.Equal(0, RankingMetrics.RecallAtK(ranked, Relevant, 10));
        Assert.Equal(0, RankingMetrics.ReciprocalRank(ranked, Relevant, 10));
        Assert.Equal(0, RankingMetrics.NdcgAtK(ranked, Relevant, 10));
    }

    [Fact]
    public void AllMetrics_EmptyRanking_AreZero()
    {
        Assert.Equal(0, RankingMetrics.RecallAtK([], Relevant, 10));
        Assert.Equal(0, RankingMetrics.ReciprocalRank([], Relevant, 10));
        Assert.Equal(0, RankingMetrics.NdcgAtK([], Relevant, 10));
    }

    [Fact]
    public void NdcgAtK_KSmallerThanRelevantCount_IdealIsCutAtK()
    {
        // Ідеал для k=1 — лише найкращий документ, тож a на першому місці дає 1.
        Assert.Equal(1, RankingMetrics.NdcgAtK(["a", "b"], Relevant, 1), precision: 10);
    }

    [Fact]
    public void Metrics_NoRelevantDocuments_Throw()
    {
        Assert.Throws<ArgumentException>(() => RankingMetrics.RecallAtK(Ranked, new Dictionary<string, int>(), 10));
    }

    [Fact]
    public void Metrics_DuplicateDocumentsInRanking_Throw()
    {
        Assert.Throws<ArgumentException>(() => RankingMetrics.NdcgAtK(["a", "a"], Relevant, 10));
    }

    [Fact]
    public void Metrics_NonPositiveK_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RankingMetrics.RecallAtK(Ranked, Relevant, 0));
    }
}
