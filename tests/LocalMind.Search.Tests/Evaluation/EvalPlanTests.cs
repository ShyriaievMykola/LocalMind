using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using LocalMind.Search.Evaluation;

namespace LocalMind.Search.Tests.Evaluation;

public class EvalPlanTests
{
    private static readonly SearchMode[] Hybrid = [SearchMode.Hybrid];

    [Fact]
    public void Build_RunsWithoutChunkingAndModel_ShareOneGroupWithoutOverrides()
    {
        var groups = EvalPlan.Build([new EvalRun("a", Hybrid, 10), new EvalRun("b", Hybrid, 5)]);

        var group = Assert.Single(groups);
        Assert.Equal(["a", "b"], group.Runs.Select(r => r.Name));
        Assert.Empty(group.ConfigOverrides);
    }

    [Fact]
    public void Build_SameChunkingAndModel_ShareAnIndex_ModelNameIgnoresCase()
    {
        var chunking = new EvalChunking(ChunkingStrategy.FixedSize, 128, 15);

        var groups = EvalPlan.Build(
        [
            new EvalRun("a", Hybrid, 10, chunking, "multilingual-e5-base"),
            new EvalRun("b", Hybrid, 5, chunking with { }, "MULTILINGUAL-E5-BASE"),
        ]);

        Assert.Single(groups);
    }

    [Fact]
    public void Build_DifferentChunkingOrModel_GivesSeparateGroupsInConfigOrder()
    {
        var groups = EvalPlan.Build(
        [
            new EvalRun("small", Hybrid, 10, new EvalChunking(ChunkSizeTokens: 64)),
            new EvalRun("default", Hybrid, 10),
            new EvalRun("small-again", Hybrid, 10, new EvalChunking(ChunkSizeTokens: 64)),
            new EvalRun("other-model", Hybrid, 10, Model: "multilingual-e5-base"),
            new EvalRun("overlap", Hybrid, 10, new EvalChunking(ChunkSizeTokens: 64, OverlapPercent: 0)),
        ]);

        Assert.Equal(4, groups.Count);
        Assert.Equal(["small", "small-again"], groups[0].Runs.Select(r => r.Name));
        Assert.Equal(["default"], groups[1].Runs.Select(r => r.Name));
        Assert.Equal(["other-model"], groups[2].Runs.Select(r => r.Name));
        Assert.Equal(["overlap"], groups[3].Runs.Select(r => r.Name));
    }

    [Fact]
    public void Build_ChunkingOverrides_UseOnlyTheGivenFieldsWithInvariantNumbers()
    {
        var groups = EvalPlan.Build([new EvalRun("a", Hybrid, 10, new EvalChunking(ChunkingStrategy.Paragraph, 128, null))]);

        var overrides = Assert.Single(groups).ConfigOverrides;

        Assert.Equal("Paragraph", overrides["LocalMind:Chunking:Strategy"]);
        Assert.Equal("128", overrides["LocalMind:Chunking:ChunkSizeTokens"]);
        Assert.False(overrides.ContainsKey("LocalMind:Chunking:OverlapPercent"));
        Assert.False(overrides.ContainsKey("LocalMind:Embedding:ModelName"));
    }

    [Fact]
    public void Build_Model_SetsNameAndClearsPathsSoCatalogSuppliesThem()
    {
        var groups = EvalPlan.Build([new EvalRun("a", Hybrid, 10, Model: "multilingual-e5-base")]);

        var overrides = Assert.Single(groups).ConfigOverrides;

        Assert.Equal("multilingual-e5-base", overrides["LocalMind:Embedding:ModelName"]);
        Assert.Equal(string.Empty, overrides["LocalMind:Embedding:ModelPath"]);
        Assert.Equal(string.Empty, overrides["LocalMind:Embedding:TokenizerPath"]);
    }
}
