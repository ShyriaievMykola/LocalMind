using LocalMind.Core.Models;
using LocalMind.Search.Hybrid;

namespace LocalMind.Search.Tests;

public class ReciprocalRankFusionTests
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = new("00000000-0000-0000-0000-00000000000d");

    private static List<ScoredChunk> Ranking(params Guid[] ids) =>
        [.. ids.Select((id, i) => new ScoredChunk(id, 100 - i))];

    [Fact]
    public void Fuse_HandComputedExample()
    {
        // k = 60. Keyword: A, B, C. Semantic: B, D, A.
        // A = 1/61 + 1/63 ≈ 0.032266; B = 1/62 + 1/61 ≈ 0.032522; C = 1/63 ≈ 0.015873; D = 1/62 ≈ 0.016129.
        var fused = ReciprocalRankFusion.Fuse([Ranking(A, B, C), Ranking(B, D, A)], k: 60, topK: 10);

        Assert.Equal([B, A, D, C], fused.Select(r => r.ChunkId));
        Assert.Equal(1.0 / 62 + 1.0 / 61, fused[0].Score, precision: 12);
        Assert.Equal(1.0 / 61 + 1.0 / 63, fused[1].Score, precision: 12);
        Assert.Equal(1.0 / 62, fused[2].Score, precision: 12);
        Assert.Equal(1.0 / 63, fused[3].Score, precision: 12);
    }

    [Fact]
    public void Fuse_IgnoresOriginalScores_UsesOnlyRanks()
    {
        var keyword = new List<ScoredChunk> { new(A, 1000), new(B, 999) };
        var semantic = new List<ScoredChunk> { new(B, 0.01), new(A, 0.001) };

        var fused = ReciprocalRankFusion.Fuse([keyword, semantic], k: 60, topK: 10);

        Assert.Equal(fused[0].Score, fused[1].Score, precision: 12);
    }

    [Fact]
    public void Fuse_EqualScoresAndRanks_OrderIsStable()
    {
        var fused = ReciprocalRankFusion.Fuse([Ranking(A, B), Ranking(B, A)], k: 60, topK: 10);

        Assert.Equal(fused[0].Score, fused[1].Score, precision: 12);
        Assert.Equal([A, B], fused.Select(r => r.ChunkId));
    }

    [Fact]
    public void Fuse_EqualScores_BetterBestRankFirst()
    {
        // k = 1: C — лише перше місце, 1/2; D — двічі третє, 1/4 + 1/4 = 1/2.
        var fused = ReciprocalRankFusion.Fuse([Ranking(C, A, D), Ranking(B, A, D)], k: 1, topK: 10);

        var c = fused.Single(r => r.ChunkId == C);
        var d = fused.Single(r => r.ChunkId == D);
        Assert.Equal(c.Score, d.Score, precision: 12);
        Assert.True(fused.ToList().IndexOf(c) < fused.ToList().IndexOf(d));
    }

    [Fact]
    public void Fuse_RespectsTopKAndSmallerK()
    {
        var fused = ReciprocalRankFusion.Fuse([Ranking(A, B, C)], k: 1, topK: 2);

        Assert.Equal([A, B], fused.Select(r => r.ChunkId));
        Assert.Equal(0.5, fused[0].Score, precision: 12);
    }

    [Fact]
    public void Fuse_DuplicateInOneList_CountedOnce()
    {
        var fused = ReciprocalRankFusion.Fuse([Ranking(A, A, B)], k: 60, topK: 10);

        Assert.Equal(1.0 / 61, fused[0].Score, precision: 12);
        Assert.Equal(1.0 / 62, fused[1].Score, precision: 12);
    }

    [Fact]
    public void Fuse_EmptyLists_ReturnsEmpty()
    {
        Assert.Empty(ReciprocalRankFusion.Fuse([[], []], k: 60, topK: 10));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(60, 0)]
    public void Fuse_InvalidArguments_Throw(int k, int topK)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReciprocalRankFusion.Fuse([], k, topK));
    }
}
