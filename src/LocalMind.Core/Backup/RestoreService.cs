using System.Security.Cryptography;
using LocalMind.Core.Backup.Crypto;
using LocalMind.Core.Errors;
using LocalMind.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Core.Backup;

/// <summary>
/// Відновлює базу з бекапу (B-08): download → розшифрування з перевіркою тегу → перевірка, що це SQLite →
/// атомарна заміна файла бази. Будь-яка помилка до заміни лишає поточну базу без змін.
/// </summary>
/// <remarks>
/// Перед викликом хост має зупинити роботу з базою (індексацію, пошук): відкриті підключення
/// не дадуть замінити файл. Після відновлення хост перезавантажує векторний індекс.
/// </remarks>
public sealed class RestoreService(IDbContextFactory<LocalMindDbContext> contexts, BackupApiClient api)
{
    /// <summary>Завантажує бекап і замінює ним поточну базу.</summary>
    /// <exception cref="BackupDecryptionException">Неправильний пароль або бекап пошкоджено.</exception>
    /// <exception cref="BackupApiException">Бекапу немає або сервер недоступний.</exception>
    public async Task RestoreAsync(Guid backupId, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var encrypted = await api.DownloadBackupAsync(backupId, cancellationToken).ConfigureAwait(false);
        await RestoreFromEnvelopeAsync(encrypted, password, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Розшифровує конверт і атомарно підміняє ним файл бази.</summary>
    internal async Task RestoreFromEnvelopeAsync(byte[] encrypted, string password, CancellationToken cancellationToken)
    {
        var plaintext = await Task.Run(() => AesGcmCipher.Decrypt(EncryptedEnvelope.Parse(encrypted), password), cancellationToken)
            .ConfigureAwait(false);

        string databasePath;
        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            databasePath = BackupService.DatabasePath(context);
        }

        var tempPath = Path.Combine(Path.GetDirectoryName(databasePath)!, $".restore-{Guid.NewGuid():N}.tmp");
        try
        {
            try
            {
                await File.WriteAllBytesAsync(tempPath, plaintext, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            await VerifyDatabaseAsync(tempPath, cancellationToken).ConfigureAwait(false);

            // Пул тримає файл бази відкритим — без цього заміна на Windows не вдасться.
            SqliteConnection.ClearAllPools();
            File.Move(tempPath, databasePath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }

        // Бекап міг бути зроблений старішою версією застосунку.
        var migrated = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (migrated.ConfigureAwait(false))
        {
            await migrated.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task VerifyDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        try
        {
            var connection = new SqliteConnection(connectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check";
                if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not "ok")
                {
                    throw new LocalMindException("Бекап містить пошкоджену базу — відновлення скасовано.");
                }
            }
        }
        catch (SqliteException ex)
        {
            throw new LocalMindException("Бекап не є базою LocalMind — відновлення скасовано.", ex);
        }
    }
}
