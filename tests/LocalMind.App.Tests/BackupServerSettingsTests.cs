using LocalMind.App.Services;
using LocalMind.Core.Backup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace LocalMind.App.Tests;

public sealed class BackupServerSettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-backup-settings-").FullName;

    private string SettingsPath => Path.Combine(_folder, "nested", "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Constructor_NoUrl_IsNotConfigured()
    {
        var settings = Create(null);

        Assert.Null(settings.ActiveUrl);
        Assert.Equal(string.Empty, settings.SavedUrl);
        Assert.False(settings.RestartRequired);
    }

    [Fact]
    public void Constructor_UrlFromConfiguration_IsActiveAndSaved()
    {
        var settings = Create("https://backup.example.com");

        Assert.Equal(new Uri("https://backup.example.com/"), settings.ActiveUrl);
        Assert.Equal("https://backup.example.com/", settings.SavedUrl);
        Assert.False(settings.RestartRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://backup.example.com")]
    [InlineData("https://backup.example.com/localmind")]
    [InlineData("http://localhost:5133")]
    [InlineData("http://127.0.0.1:5133")]
    public void Validate_AcceptableAddress_HasNoError(string url) => Assert.Null(Create(null).Validate(url));

    [Theory]
    [InlineData("backup.example.com")]
    [InlineData("http://backup.example.com")]
    [InlineData("ftp://backup.example.com")]
    [InlineData("https://user:pw@backup.example.com")]
    public void Validate_UnacceptableAddress_ExplainsWhat(string url) => Assert.NotNull(Create(null).Validate(url));

    [Fact]
    public async Task Save_WritesAddressThatConfigurationReadsAfterRestart()
    {
        var settings = Create(null);

        await settings.SaveAsync(" http://localhost:5133 ");

        var configuration = new ConfigurationBuilder().AddJsonFile(SettingsPath).Build();
        var options = configuration.GetSection(BackupClientOptions.SectionName).Get<BackupClientOptions>()!;
        Assert.Equal(new Uri("http://localhost:5133/"), options.ServerUrl);
        Assert.Equal("http://localhost:5133/", settings.SavedUrl);
        Assert.True(settings.RestartRequired);
        Assert.Null(settings.ActiveUrl);
    }

    [Fact]
    public async Task Save_KeepsOtherSectionsAndKeys()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, """
            { "LocalMind": { "Search": { "DefaultTopK": 25 }, "Backup": { "TokenFilePath": "session.bin" } } }
            """);

        await Create(null).SaveAsync("https://backup.example.com");

        var configuration = new ConfigurationBuilder().AddJsonFile(SettingsPath).Build();
        Assert.Equal("25", configuration["LocalMind:Search:DefaultTopK"]);
        Assert.Equal("session.bin", configuration["LocalMind:Backup:TokenFilePath"]);
        Assert.Equal("https://backup.example.com/", configuration["LocalMind:Backup:ServerUrl"]);
    }

    [Fact]
    public async Task Save_Empty_RemovesAddress()
    {
        var settings = Create("https://backup.example.com");

        await settings.SaveAsync(string.Empty);

        var configuration = new ConfigurationBuilder().AddJsonFile(SettingsPath).Build();
        Assert.Equal(string.Empty, configuration["LocalMind:Backup:ServerUrl"]);
        Assert.Equal(string.Empty, settings.SavedUrl);
        Assert.True(settings.RestartRequired);
    }

    [Fact]
    public async Task Save_InvalidAddress_ThrowsAndDoesNotWriteFile()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Create(null).SaveAsync("http://backup.example.com"));

        Assert.False(File.Exists(SettingsPath));
    }

    private BackupServerSettings Create(string? url) =>
        new(Options.Create(new BackupClientOptions { ServerUrl = url is null ? null : new Uri(url) }),
            new UserSettingsFile(SettingsPath));
}
