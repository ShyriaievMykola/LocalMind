using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using LocalMind.Search.Evaluation;
using LocalMind.Search.Tests.Configuration;
using Microsoft.Extensions.Configuration;

namespace LocalMind.Search.Tests.Evaluation;

public class EvalPlanTests
{
    private static readonly SearchMode[] Hybrid = [SearchMode.Hybrid];

    // Конфігурація застосунку: модель за замовчуванням multilingual-e5-small, нарізка 256 / 15 %, fixedSize.
    internal static IConfigurationRoot AppConfiguration() =>
        new ConfigurationBuilder()
            .AddJsonFile(ModelCatalogTests.RepositoryFile("src", "LocalMind.Cli", "appsettings.json"), optional: false)
            .Build();

    [Fact]
    public void Build_RunsWithoutChunkingAndModel_ShareOneGroupWithEffectiveDefaults()
    {
        var groups = EvalPlan.Build([new EvalRun("a", Hybrid, 10), new EvalRun("b", Hybrid, 5)], AppConfiguration());

        var group = Assert.Single(groups);
        Assert.Equal(["a", "b"], group.Runs.Select(r => r.Name));
        Assert.Equal(new EvalSetup("multilingual-e5-small", ChunkingStrategy.FixedSize, 256, 15), group.Setup);
        Assert.Empty(group.Problems);
        Assert.False(group.ConfigOverrides.ContainsKey("LocalMind:Embedding:ModelName"));
    }

    [Fact]
    public void Build_EmptyChunkingExplicitDefaultsAndExplicitDefaultModel_AreTheSameCombination()
    {
        var groups = EvalPlan.Build(
        [
            new EvalRun("missing", Hybrid, 10),
            new EvalRun("empty", Hybrid, 10, new EvalChunking()),
            new EvalRun("explicit", Hybrid, 10, new EvalChunking(ChunkingStrategy.FixedSize, 256, 15), "Multilingual-E5-Small"),
        ], AppConfiguration());

        var group = Assert.Single(groups);
        Assert.Equal(["missing", "empty", "explicit"], group.Runs.Select(r => r.Name));
        Assert.Equal(string.Empty, group.ConfigOverrides.GetValueOrDefault("LocalMind:Embedding:ModelPath", string.Empty));
    }

    [Fact]
    public void Build_DifferentEffectiveChunkingOrModel_GivesSeparateGroupsInConfigOrder()
    {
        var groups = EvalPlan.Build(
        [
            new EvalRun("small", Hybrid, 10, new EvalChunking(ChunkSizeTokens: 64)),
            new EvalRun("default", Hybrid, 10),
            new EvalRun("small-again", Hybrid, 10, new EvalChunking(ChunkSizeTokens: 64)),
            new EvalRun("other-model", Hybrid, 10, Model: "multilingual-e5-base"),
            new EvalRun("overlap", Hybrid, 10, new EvalChunking(ChunkSizeTokens: 64, OverlapPercent: 0)),
        ], AppConfiguration());

        Assert.Equal(4, groups.Count);
        Assert.Equal(["small", "small-again"], groups[0].Runs.Select(r => r.Name));
        Assert.Equal(["default"], groups[1].Runs.Select(r => r.Name));
        Assert.Equal(["other-model"], groups[2].Runs.Select(r => r.Name));
        Assert.Equal(["overlap"], groups[3].Runs.Select(r => r.Name));
    }

    [Fact]
    public void Build_Overrides_CarryEffectiveChunkingWithInvariantNumbers()
    {
        var groups = EvalPlan.Build([new EvalRun("a", Hybrid, 10, new EvalChunking(ChunkingStrategy.Paragraph, 128, null))], AppConfiguration());

        var overrides = Assert.Single(groups).ConfigOverrides;

        Assert.Equal("Paragraph", overrides["LocalMind:Chunking:Strategy"]);
        Assert.Equal("128", overrides["LocalMind:Chunking:ChunkSizeTokens"]);
        Assert.Equal("15", overrides["LocalMind:Chunking:OverlapPercent"]);
    }

    [Fact]
    public void Build_OtherCatalogModel_SetsNameClearsPathsAndTakesLimitsFromCatalog()
    {
        var groups = EvalPlan.Build([new EvalRun("a", Hybrid, 10, Model: "paraphrase-multilingual-MiniLM-L12-v2", Chunking: new EvalChunking(ChunkSizeTokens: 120))], AppConfiguration());

        var group = Assert.Single(groups);

        Assert.Equal("paraphrase-multilingual-MiniLM-L12-v2", group.ConfigOverrides["LocalMind:Embedding:ModelName"]);
        Assert.Equal(string.Empty, group.ConfigOverrides["LocalMind:Embedding:ModelPath"]);
        Assert.Equal(string.Empty, group.ConfigOverrides["LocalMind:Embedding:TokenizerPath"]);
        Assert.Equal("models/paraphrase-multilingual-MiniLM-L12-v2/model.onnx", group.ModelPath);
        Assert.Empty(group.Problems);
    }

    [Fact]
    public void Build_ChunkLargerThanModelLimit_IsReportedBeforeAnythingRuns()
    {
        var groups = EvalPlan.Build(
        [
            new EvalRun("ok", Hybrid, 10),
            new EvalRun("p-minilm", Hybrid, 10, Model: "paraphrase-multilingual-MiniLM-L12-v2"),
        ], AppConfiguration());

        Assert.Empty(groups[0].Problems);
        var problem = Assert.Single(groups[1].Problems);
        Assert.Contains("p-minilm", problem, StringComparison.Ordinal);
        Assert.Contains("256", problem, StringComparison.Ordinal);
        Assert.Contains("128", problem, StringComparison.Ordinal);
        Assert.Contains("~120", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_UnknownModel_IsReportedWithCatalogNames()
    {
        var groups = EvalPlan.Build([new EvalRun("x", Hybrid, 10, Model: "no-such-model")], AppConfiguration());

        var problem = Assert.Single(Assert.Single(groups).Problems);

        Assert.Contains("no-such-model", problem, StringComparison.Ordinal);
        Assert.Contains("multilingual-e5-base", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresModel_OnlyWhenSomeRunUsesSemanticOrHybrid()
    {
        var keyword = EvalPlan.Build([new EvalRun("k", [SearchMode.Keyword], 10)], AppConfiguration());
        var hybrid = EvalPlan.Build([new EvalRun("h", Hybrid, 10)], AppConfiguration());

        Assert.False(Assert.Single(keyword).RequiresModel);
        Assert.True(Assert.Single(hybrid).RequiresModel);
    }
}
