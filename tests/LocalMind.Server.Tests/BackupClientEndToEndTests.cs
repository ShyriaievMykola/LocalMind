using LocalMind.Core.Backup;
using LocalMind.Core.Errors;
using LocalMind.Core.Storage;
using LocalMind.Core.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Server.Tests;

/// <summary>Клієнт бекапу з Core проти справжнього сервера в пам'яті: реєстрація → бекап → відновлення.</summary>
public sealed class BackupClientEndToEndTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>, IDisposable
{
    private const string AccountPassword = "довгий пароль облікового запису";
    private const string EncryptionPassword = "окремий пароль шифрування";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "localmind-e2e", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task BackupThenRestore_RoundTripsThroughServer()
    {
        var contexts = await CreateDatabaseAsync("/notes/original");
        var api = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await api.RegisterAsync(email, AccountPassword);
        await api.LoginAsync(email, AccountPassword);

        var info = await new BackupService(contexts, api).CreateBackupAsync(EncryptionPassword);
        await using (var context = contexts.CreateDbContext())
        {
            context.SourceFolders.RemoveRange(context.SourceFolders);
            context.SourceFolders.Add(new SourceFolderEntity { Path = "/notes/after-backup" });
            await context.SaveChangesAsync();
        }

        Assert.Contains(info, await api.ListBackupsAsync());
        await new RestoreService(contexts, api).RestoreAsync(info.Id, EncryptionPassword);

        await using var restored = contexts.CreateDbContext();
        Assert.Equal(["/notes/original"], await restored.SourceFolders.Select(f => f.Path).ToListAsync());
    }

    [Fact]
    public async Task Restore_WrongPassword_KeepsDatabase()
    {
        var contexts = await CreateDatabaseAsync("/notes/original");
        var api = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await api.RegisterAsync(email, AccountPassword);
        await api.LoginAsync(email, AccountPassword);
        var info = await new BackupService(contexts, api).CreateBackupAsync(EncryptionPassword);
        SqliteConnection.ClearAllPools();
        var before = await File.ReadAllBytesAsync(Path.Combine(_directory, "localmind.db"));

        await Assert.ThrowsAsync<BackupDecryptionException>(
            () => new RestoreService(contexts, api).RestoreAsync(info.Id, "не той пароль"));

        SqliteConnection.ClearAllPools();
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_directory, "localmind.db")));
    }

    [Fact]
    public async Task Logout_ThenRequest_RequiresSignIn()
    {
        var api = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await api.RegisterAsync(email, AccountPassword);
        await api.LoginAsync(email, AccountPassword);

        await api.LogoutAsync();

        var error = await Assert.ThrowsAsync<BackupApiException>(() => api.ListBackupsAsync());
        Assert.True(error.IsSessionExpired);
    }

    private BackupApiClient CreateClient() => new(factory.CreateClient(), new InMemoryTokenStore(), TimeProvider.System);

    private async Task<IDbContextFactory<LocalMindDbContext>> CreateDatabaseAsync(string folder)
    {
        Directory.CreateDirectory(_directory);
        var contexts = new FileContextFactory(Path.Combine(_directory, "localmind.db"));
        await using var context = contexts.CreateDbContext();
        await context.Database.MigrateAsync();
        context.SourceFolders.Add(new SourceFolderEntity { Path = folder });
        await context.SaveChangesAsync();
        return contexts;
    }

    private sealed class FileContextFactory(string path) : IDbContextFactory<LocalMindDbContext>
    {
        public LocalMindDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<LocalMindDbContext>().UseSqlite($"Data Source={path}").Options);
    }
}
