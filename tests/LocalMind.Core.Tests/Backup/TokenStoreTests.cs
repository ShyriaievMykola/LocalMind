using System.Text;
using LocalMind.Core.Backup;
using LocalMind.Core.Backup.Dtos;

namespace LocalMind.Core.Tests.Backup;

public sealed class TokenStoreTests : IDisposable
{
    private const string RefreshToken = "REFRESH-TOKEN-ASCII-MARKER";

    private static readonly TokenResponse Tokens =
        new("ACCESS-TOKEN-ASCII-MARKER", DateTimeOffset.UnixEpoch, RefreshToken, DateTimeOffset.UnixEpoch.AddDays(14));

    private readonly string _path = Path.Combine(Path.GetTempPath(), "localmind-token-tests", $"{Guid.NewGuid():N}.bin");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void InMemory_SaveLoadClear()
    {
        var store = new InMemoryTokenStore();

        store.Save(Tokens);
        Assert.Equal(Tokens, store.Load());
        store.Clear();
        Assert.Null(store.Load());
    }

    [Fact]
    public void Dpapi_SurvivesRestartAndFileHasNoPlaintext()
    {
        // DPAPI є лише на Windows; на інших ОС клієнт тримає токени в пам'яті (DR-031).
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        new DpapiTokenStore(_path).Save(Tokens);
        var reopened = new DpapiTokenStore(_path);

        Assert.Equal(Tokens, reopened.Load());
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(_path));
        Assert.DoesNotContain(RefreshToken, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("ACCESS-TOKEN", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Dpapi_ClearDeletesFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new DpapiTokenStore(_path);
        store.Save(Tokens);

        store.Clear();

        Assert.False(File.Exists(_path));
        Assert.Null(new DpapiTokenStore(_path).Load());
    }

    [Fact]
    public void Dpapi_CorruptedFile_TreatedAsSignedOut()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, [1, 2, 3, 4]);

        Assert.Null(new DpapiTokenStore(_path).Load());
        Assert.False(File.Exists(_path));
    }
}
