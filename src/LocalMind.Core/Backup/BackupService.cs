using System.Security.Cryptography;
using LocalMind.Core.Backup.Crypto;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Core.Backup;

/// <summary>
/// Створює бекап (B-07): консистентний знімок SQLite через <c>VACUUM INTO</c> → шифрування паролем → upload.
/// <c>VACUUM INTO</c> читає базу в одній транзакції, тож знімок цілісний навіть під час індексації.
/// </summary>
public sealed class BackupService(IDbContextFactory<LocalMindDbContext> contexts, BackupApiClient api)
{
    /// <summary>Шифрує поточну базу паролем і завантажує на сервер.</summary>
    /// <exception cref="ArgumentException">Порожній пароль шифрування.</exception>
    /// <exception cref="Errors.BackupApiException">Сервер відхилив бекап або недоступний.</exception>
    public async Task<BackupInfo> CreateBackupAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var encrypted = await CreateEncryptedSnapshotAsync(password, cancellationToken).ConfigureAwait(false);
        return await api.UploadBackupAsync(encrypted, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Знімок бази, зашифрований паролем (конверт <see cref="EncryptedEnvelope"/>).</summary>
    internal async Task<byte[]> CreateEncryptedSnapshotAsync(string password, CancellationToken cancellationToken)
    {
        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var databasePath = DatabasePath(context);

            // Тимчасовий відкритий знімок — поруч із базою: той самий диск і ті самі права доступу, що й в оригіналу.
            var snapshotPath = Path.Combine(Path.GetDirectoryName(databasePath)!, $".backup-{Guid.NewGuid():N}.tmp");
            try
            {
                await context.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [snapshotPath], cancellationToken)
                    .ConfigureAwait(false);
                var plaintext = await File.ReadAllBytesAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
                try
                {
                    var envelope = await Task.Run(() => AesGcmCipher.Encrypt(plaintext, password), cancellationToken)
                        .ConfigureAwait(false);
                    return envelope.ToBytes();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                File.Delete(snapshotPath);
            }
        }
    }

    internal static string DatabasePath(LocalMindDbContext context) =>
        Path.GetFullPath(context.Database.GetDbConnection().DataSource);
}
