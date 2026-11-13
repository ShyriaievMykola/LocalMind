using LocalMind.Core.Backup;

namespace LocalMind.Core.Tests.Backup;

public class BackupClientOptionsTests
{
    [Fact]
    public void DefaultTokenFilePath_IsSessionFileInLocalMindFolderOfUserData()
    {
        var path = BackupClientOptions.DefaultTokenFilePath();

        Assert.Equal("session.bin", Path.GetFileName(path));
        Assert.Equal("LocalMind", Path.GetFileName(Path.GetDirectoryName(path)));
        var userData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        Assert.StartsWith(userData, path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://backup.example.com", true)]
    [InlineData("https://backup.example.com/localmind", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("http://127.0.0.1:5000", true)]
    [InlineData("http://[::1]:5000", true)]
    [InlineData("http://backup.example.com", false)]
    [InlineData("http://192.168.0.10:5000", false)]
    [InlineData("ftp://localhost", false)]
    public void IsValidServerUrl_AllowsHttpsAndLoopbackHttpOnly(string url, bool expected) =>
        Assert.Equal(expected, BackupClientOptions.IsValidServerUrl(new Uri(url)));

    [Fact]
    public void IsValidServerUrl_NullOrRelative_IsInvalid()
    {
        Assert.False(BackupClientOptions.IsValidServerUrl(null));
        Assert.False(BackupClientOptions.IsValidServerUrl(new Uri("api/v1", UriKind.Relative)));
    }

    [Fact]
    public void WithTrailingSlash_Null_StaysNull() => Assert.Null(BackupClientOptions.WithTrailingSlash(null));
}
