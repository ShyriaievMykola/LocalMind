using LocalMind.Core.Configuration;

namespace LocalMind.Core.Tests.Configuration;

public class IngestionOptionsValidatorTests
{
    private readonly IngestionOptionsValidator _validator = new();

    [Fact]
    public void Validate_Defaults_Succeed()
    {
        Assert.True(_validator.Validate(null, new IngestionOptions()).Succeeded);
        Assert.Equal(50L * 1024 * 1024, new IngestionOptions().MaxFileSizeBytes);
    }

    [Theory]
    [InlineData(0, 10, 10, "MaxFileSizeBytes")]
    [InlineData(10, 0, 10, "MaxArchiveEntries")]
    [InlineData(10, 10, -1, "MaxArchiveUncompressedBytes")]
    public void Validate_NonPositiveLimit_FailsWithOptionName(long size, int entries, long uncompressed, string name)
    {
        var options = new IngestionOptions
        {
            MaxFileSizeBytes = size,
            MaxArchiveEntries = entries,
            MaxArchiveUncompressedBytes = uncompressed,
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(name, StringComparison.Ordinal));
    }
}
