using LocalMind.Core.Configuration;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using LocalMind.Search.Evaluation;
using LocalMind.Search.Tests.Configuration;
using Microsoft.Extensions.Configuration;

namespace LocalMind.Search.Tests.Evaluation;

public sealed class EvalExperimentTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-exp-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Load_ValidConfig_ReadsRunsWithDefaultK()
    {
        var runs = Load("""
            {"runs": [
              {"name": "bm25", "modes": ["keyword"], "k": 5},
              {"name": "all", "modes": ["keyword", "semantic", "hybrid", "keyword"]}
            ]}
            """);

        Assert.Equal(2, runs.Count);
        Assert.Equal(new EvalRun("bm25", [SearchMode.Keyword], 5), runs[0], new RunComparer());
        Assert.Equal([SearchMode.Keyword, SearchMode.Semantic, SearchMode.Hybrid], runs[1].Modes);
        Assert.Equal(EvalExperiment.DefaultK, runs[1].K);
    }

    [Fact]
    public void Load_ChunkingAndModel_AreReadAndOptional()
    {
        var runs = Load("""
            {"runs": [
              {"name": "plain", "modes": ["semantic"]},
              {"name": "full", "modes": ["hybrid"], "model": " multilingual-e5-base ",
               "chunking": {"strategy": "paragraph", "chunkSizeTokens": 128, "overlapPercent": 20}},
              {"name": "partial", "modes": ["hybrid"], "chunking": {"chunkSizeTokens": 64}}
            ]}
            """);

        Assert.Null(runs[0].Chunking);
        Assert.Null(runs[0].Model);
        Assert.Equal("multilingual-e5-base", runs[1].Model);
        Assert.Equal(new EvalChunking(ChunkingStrategy.Paragraph, 128, 20), runs[1].Chunking);
        Assert.Equal(new EvalChunking(null, 64, null), runs[2].Chunking);
    }

    [Theory]
    [InlineData("""{"runs": [{"name": "a", "modes": ["keyword"], "chunking": {"chunkSizeTokens": 0}}]}""", "chunkSizeTokens")]
    [InlineData("""{"runs": [{"name": "a", "modes": ["keyword"], "chunking": {"overlapPercent": -1}}]}""", "overlapPercent")]
    [InlineData("""{"runs": [{"name": "a", "modes": ["keyword"], "chunking": {"overlapPercent": 51}}]}""", "overlapPercent")]
    [InlineData("""{"runs": [{"name": "a", "modes": ["keyword"], "chunking": {"strategy": "random"}}]}""", "некоректний JSON")]
    [InlineData("""{"runs": [{"name": "a", "modes": ["keyword"], "model": "  "}]}""", "model порожня")]
    public void Load_BrokenChunkingOrModel_ThrowsWithReason(string json, string reason)
    {
        var exception = Assert.Throws<LocalMindException>(() => Load(json));

        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("e2.json")]
    [InlineData("e3.json")]
    public void ExampleExperiments_AreValidAndUseOnlyCatalogModels(string file)
    {
        var runs = EvalExperiment.Load(ModelCatalogTests.RepositoryFile("eval", "experiments", file));
        var catalog = new ModelCatalogOptions();
        new ConfigurationBuilder()
            .AddJsonFile(ModelCatalogTests.RepositoryFile("src", "LocalMind.Cli", "appsettings.json"))
            .Build()
            .GetSection(ModelCatalogOptions.SectionName)
            .Bind(catalog);

        Assert.NotEmpty(runs);
        Assert.All(runs.Where(run => run.Model is not null), run => Assert.NotNull(catalog.Find(run.Model)));
        Assert.True(EvalPlan.Build(runs).Count > 1, "Експеримент має містити кілька комбінацій нарізки чи моделі.");
    }

    [Theory]
    [InlineData("""{"runs": []}""", "runs")]
    [InlineData("{}", "runs")]
    [InlineData("""{"runs": [{"modes": ["keyword"]}]}""", "name")]
    [InlineData("""{"runs": [{"name": "a"}]}""", "modes")]
    [InlineData("""{"runs": [{"name": "a", "modes": []}]}""", "modes")]
    [InlineData("""{"runs": [{"name": "a", "modes": ["fuzzy"]}]}""", "некоректний JSON")]
    [InlineData("""{"runs": [{"name": "a", "modes": ["keyword"], "k": 0}]}""", "k має бути")]
    [InlineData("""{"runs": [{"name": "a", "modes": ["keyword"]}, {"name": "a", "modes": ["hybrid"]}]}""", "повторюється")]
    [InlineData("не json", "некоректний JSON")]
    public void Load_BrokenConfig_ThrowsWithReason(string json, string reason)
    {
        var exception = Assert.Throws<LocalMindException>(() => Load(json));

        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Assert.Throws<LocalMindException>(() => EvalExperiment.Load(Path.Combine(_folder, "немає.json")));
    }

    private IReadOnlyList<EvalRun> Load(string json)
    {
        var path = Path.Combine(_folder, "experiment.json");
        File.WriteAllText(path, json);
        return EvalExperiment.Load(path);
    }

    private sealed class RunComparer : IEqualityComparer<EvalRun>
    {
        public bool Equals(EvalRun? x, EvalRun? y) =>
            x is not null && y is not null && x.Name == y.Name && x.K == y.K && x.Modes.SequenceEqual(y.Modes);

        public int GetHashCode(EvalRun obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
