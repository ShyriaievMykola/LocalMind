using System.Globalization;
using LocalMind.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LocalMind.Core.Tests.Configuration;

public class ChunkingOptionsTests
{
    private const string Section = ChunkingOptions.SectionName;

    private static ServiceProvider BuildProvider(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>($"{Section}:{v.Key}", v.Value)))
            .Build();

        return new ServiceCollection().AddLocalMindCore(configuration).BuildServiceProvider();
    }

    [Fact]
    public void EmptyConfiguration_UsesValidDefaults()
    {
        using var provider = BuildProvider();

        var options = provider.GetRequiredService<IOptions<ChunkingOptions>>().Value;

        Assert.Equal(ChunkingStrategy.FixedSize, options.Strategy);
        Assert.Equal(256, options.ChunkSizeTokens);
        Assert.Equal(15, options.OverlapPercent);
    }

    [Fact]
    public void Configuration_IsBoundToOptions()
    {
        using var provider = BuildProvider(("Strategy", "Paragraph"), ("ChunkSizeTokens", "512"), ("OverlapPercent", "0"));

        var options = provider.GetRequiredService<IOptions<ChunkingOptions>>().Value;

        Assert.Equal(ChunkingStrategy.Paragraph, options.Strategy);
        Assert.Equal(512, options.ChunkSizeTokens);
        Assert.Equal(0, options.OverlapPercent);
    }

    [Fact]
    public void OverlapAtMaximum_IsValid()
    {
        using var provider = BuildProvider(("OverlapPercent", ChunkingOptions.MaxOverlapPercent.ToString(CultureInfo.InvariantCulture)));

        Assert.Equal(
            ChunkingOptions.MaxOverlapPercent,
            provider.GetRequiredService<IOptions<ChunkingOptions>>().Value.OverlapPercent);
    }

    [Theory]
    [InlineData("ChunkSizeTokens", "0")]
    [InlineData("ChunkSizeTokens", "-5")]
    [InlineData("OverlapPercent", "-1")]
    [InlineData("OverlapPercent", "51")]
    [InlineData("Strategy", "7")]
    public void InvalidValue_FailsValidationWithKeyInMessage(string key, string value)
    {
        using var provider = BuildProvider((key, value));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ChunkingOptions>>().Value);

        Assert.Contains($"{Section}:{key}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidConfiguration_FailsStartupValidation()
    {
        using var provider = BuildProvider(("ChunkSizeTokens", "0"));

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void Validator_ReportsAllErrorsAtOnce()
    {
        var result = new ChunkingOptionsValidator().Validate(
            null, new ChunkingOptions { ChunkSizeTokens = 0, OverlapPercent = 90 });

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures!.Count());
    }

    [Fact]
    public void Validator_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChunkingOptionsValidator().Validate(null, null!));
    }

    [Fact]
    public void AddLocalMindCore_CalledTwice_RegistersValidatorOnce()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddLocalMindCore(configuration).AddLocalMindCore(configuration);

        Assert.Single(services, d => d.ServiceType == typeof(IValidateOptions<ChunkingOptions>));
    }

    [Fact]
    public void AddLocalMindCore_NullArguments_Throw()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddLocalMindCore(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddLocalMindCore(null!));
    }
}
