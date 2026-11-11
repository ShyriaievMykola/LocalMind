using LocalMind.Core.Backup;
using LocalMind.Core.Backup.Crypto;
using LocalMind.Core.Errors;
using LocalMind.Core.Storage;
using LocalMind.Core.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Core.Tests.Backup;

public sealed class BackupRestoreTests : IAsyncLifetime
{
    private const string Password = "пароль шифрування бекапу";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "localmind-backup-tests", Guid.NewGuid().ToString("N"));
    private FileContextFactory _contexts = null!;
    private BackupService _backup = null!;
    private RestoreService _restore = null!;

    private string DatabasePath => Path.Combine(_directory, "localmind.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _contexts = new FileContextFactory(DatabasePath);
        await using var context = _contexts.CreateDbContext();
        await context.Database.MigrateAsync();
        context.SourceFolders.Add(new SourceFolderEntity { Path = "/notes/original" });
        await context.SaveChangesAsync();

        var api = new BackupApiClient(new HttpClient(), new InMemoryTokenStore(), TimeProvider.System);
        _backup = new BackupService(_contexts, api);
        _restore = new RestoreService(_contexts, api);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Snapshot_IsEncryptedAndLeavesNoTempFiles()
    {
        var encrypted = await _backup.CreateEncryptedSnapshotAsync(Password, default);

        var plaintext = AesGcmCipher.Decrypt(EncryptedEnvelope.Parse(encrypted), Password);
        Assert.Equal("SQLite format 3\0"u8.ToArray(), plaintext[..16]);
        Assert.Equal(-1, encrypted.AsSpan().IndexOf("/notes/original"u8));
        Assert.Equal(["localmind.db"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task SnapshotThenRestore_ReturnsDatabaseToSnapshotState()
    {
        var encrypted = await _backup.CreateEncryptedSnapshotAsync(Password, default);
        await using (var context = _contexts.CreateDbContext())
        {
            context.SourceFolders.Add(new SourceFolderEntity { Path = "/notes/added-later" });
            await context.SaveChangesAsync();
        }

        await _restore.RestoreFromEnvelopeAsync(encrypted, Password, default);

        await using var restored = _contexts.CreateDbContext();
        Assert.Equal(["/notes/original"], await restored.SourceFolders.Select(f => f.Path).ToListAsync());
        Assert.Equal(["localmind.db"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Restore_WrongPassword_KeepsCurrentDatabase()
    {
        var encrypted = await _backup.CreateEncryptedSnapshotAsync(Password, default);
        var before = await ReadDatabaseAsync();

        await Assert.ThrowsAsync<BackupDecryptionException>(
            () => _restore.RestoreFromEnvelopeAsync(encrypted, "неправильний пароль", default));

        Assert.Equal(before, await ReadDatabaseAsync());
    }

    [Fact]
    public async Task Restore_CorruptedBackup_KeepsCurrentDatabase()
    {
        var encrypted = await _backup.CreateEncryptedSnapshotAsync(Password, default);
        encrypted[^20] ^= 0xFF;
        var before = await ReadDatabaseAsync();

        await Assert.ThrowsAsync<BackupDecryptionException>(
            () => _restore.RestoreFromEnvelopeAsync(encrypted, Password, default));

        Assert.Equal(before, await ReadDatabaseAsync());
    }

    [Fact]
    public async Task Restore_EncryptedNonDatabase_KeepsCurrentDatabase()
    {
        var notADatabase = AesGcmCipher.Encrypt("це не SQLite, а просто текст достатньої довжини"u8, Password).ToBytes();
        var before = await ReadDatabaseAsync();

        await Assert.ThrowsAsync<LocalMindException>(
            () => _restore.RestoreFromEnvelopeAsync(notADatabase, Password, default));

        Assert.Equal(before, await ReadDatabaseAsync());
        Assert.Equal(["localmind.db"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Restore_ValidButForeignSqlite_KeepsCurrentDatabase()
    {
        var foreign = Path.Combine(_directory, "foreign.sqlite");
        await using (var connection = new SqliteConnection($"Data Source={foreign};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Other (Id INTEGER PRIMARY KEY)";
            await command.ExecuteNonQueryAsync();
        }

        var encrypted = AesGcmCipher.Encrypt(await File.ReadAllBytesAsync(foreign), Password).ToBytes();
        File.Delete(foreign);
        var before = await ReadDatabaseAsync();

        await Assert.ThrowsAsync<LocalMindException>(
            () => _restore.RestoreFromEnvelopeAsync(encrypted, Password, default));

        Assert.Equal(before, await ReadDatabaseAsync());
        Assert.Equal(["localmind.db"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Restore_Success_LeavesNoBackupFiles()
    {
        var encrypted = await _backup.CreateEncryptedSnapshotAsync(Password, default);

        await _restore.RestoreFromEnvelopeAsync(encrypted, Password, default);

        SqliteConnection.ClearAllPools();
        Assert.DoesNotContain(Directory.GetFiles(_directory), f => Path.GetFileName(f).StartsWith(".restore-", StringComparison.Ordinal));
    }

    private async Task<byte[]> ReadDatabaseAsync()
    {
        SqliteConnection.ClearAllPools();
        return await File.ReadAllBytesAsync(DatabasePath);
    }

    private sealed class FileContextFactory(string path) : IDbContextFactory<LocalMindDbContext>
    {
        public LocalMindDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<LocalMindDbContext>().UseSqlite($"Data Source={path}").Options);
    }
}
