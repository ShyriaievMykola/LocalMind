using LocalMind.Core.Abstractions;
using LocalMind.Search.Configuration;
using LocalMind.Search.Vectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Search.Tests.Configuration;

public class VectorIndexOptionsTests
{
    [Fact]
    public void Validate_Defaults_Pass()
    {
        Assert.True(new VectorIndexOptionsValidator().Validate(null, new VectorIndexOptions()).Succeeded);
    }

    [Theory]
    [InlineData(1, 200, 64)]
    [InlineData(129, 200, 64)]
    [InlineData(16, 8, 64)]
    [InlineData(16, 200, 0)]
    public void Validate_OutOfRange_Fails(int m, int efConstruction, int efSearch)
    {
        var options = new VectorIndexOptions { M = m, EfConstruction = efConstruction, EfSearch = efSearch };

        Assert.True(new VectorIndexOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Validate_UnknownKind_Fails()
    {
        var options = new VectorIndexOptions { Kind = (VectorIndexKind)42 };

        Assert.True(new VectorIndexOptionsValidator().Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(null, typeof(BruteForceIndex))]
    [InlineData("BruteForce", typeof(BruteForceIndex))]
    [InlineData("Hnsw", typeof(HnswIndex))]
    public void AddLocalMindSearch_Kind_SelectsTheIndex(string? kind, Type expected)
    {
        var values = new Dictionary<string, string?>
        {
            ["LocalMind:Embedding:ModelName"] = "test",
            ["LocalMind:Embedding:ModelPath"] = "model.onnx",
            ["LocalMind:Embedding:TokenizerPath"] = "tokenizer.model",
            ["LocalMind:Embedding:Dimensions"] = "8",
        };
        if (kind is not null)
        {
            values[$"{VectorIndexOptions.SectionName}:Kind"] = kind;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLocalMindSearch(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsType(expected, provider.GetRequiredService<IVectorIndex>());
    }
}
