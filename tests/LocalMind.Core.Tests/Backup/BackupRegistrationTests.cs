using LocalMind.Core.Backup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LocalMind.Core.Tests.Backup;

public sealed class BackupRegistrationTests
{
    [Theory]
    [InlineData("https://backup.example.com/")]
    [InlineData("http://localhost:5133/")]
    [InlineData("http://127.0.0.1:5133/")]
    public void AddLocalMindBackup_ValidUrl_ResolvesServices(string url)
    {
        using var provider = Build(url);

        Assert.NotNull(provider.GetRequiredService<BackupService>());
        Assert.NotNull(provider.GetRequiredService<RestoreService>());
        var client = provider.GetRequiredService<BackupApiClient>();
        Assert.False(client.IsSignedIn);
        var store = provider.GetRequiredService<ITokenStore>();
        Assert.IsType(OperatingSystem.IsWindows() ? typeof(DpapiTokenStore) : typeof(InMemoryTokenStore), store);
    }

    [Theory]
    [InlineData("http://backup.example.com/")]
    [InlineData("ftp://backup.example.com/")]
    [InlineData("")]
    public void AddLocalMindBackup_InsecureOrMissingUrl_FailsValidation(string url)
    {
        using var provider = Build(url);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BackupClientOptions>>().Value);
    }

    private static ServiceProvider Build(string url)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalMind:Backup:ServerUrl"] = url,
                ["LocalMind:Backup:TokenFilePath"] = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin"),
            })
            .Build();
        return new ServiceCollection()
            .AddLocalMindCore(configuration)
            .AddLocalMindBackup(configuration)
            .BuildServiceProvider();
    }
}
