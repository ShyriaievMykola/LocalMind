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
    /// <param name="backupId">Ідентифікатор бекапу на сервері.</param>
    /// <param name="password">Пароль шифрування бекапу.</param>
    /// <param name="beforeReplace">
    /// Викликається, коли бекап уже завантажено, розшифровано й перевірено, і лише перед самою заміною файла бази:
    /// тут хост зупиняє індексацію. Не викликається, якщо пароль неправильний чи бекап пошкоджено.
    /// </param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <exception cref="BackupDecryptionException">Неправильний пароль або бекап пошкоджено.</exception>
    /// <exception cref="BackupApiException">Бекапу немає або сервер недоступний.</exception>
    public async Task RestoreAsync(
        Guid backupId, string password, Action? beforeReplace = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var encrypted = await api.DownloadBackupAsync(backupId, cancellationToken).ConfigureAwait(false);
        await RestoreFromEnvelopeAsync(encrypted, password, cancellationToken, beforeReplace).ConfigureAwait(false);
    }

    /// <summary>Розшифровує конверт і атомарно підміняє ним файл бази.</summary>
    internal async Task RestoreFromEnvelopeAsync(
        byte[] encrypted, string password, CancellationToken cancellationToken, Action? beforeReplace = null)
    {
        var plaintext = await Task.Run(() => AesGcmCipher.Decrypt(EncryptedEnvelope.Parse(encrypted), password), cancellationToken)
            .ConfigureAwait(false);

        string databasePath;
        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            databasePath = BackupService.DatabasePath(context);
        }

        var directory = Path.GetDirectoryName(databasePath)!;
        var tempPath = Path.Combine(directory, $".restore-{Guid.NewGuid():N}.tmp");
        var previousPath = Path.Combine(directory, $".restore-{Guid.NewGuid():N}.bak");
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

            beforeReplace?.Invoke();

            // Пул тримає файл бази відкритим — без цього заміна на Windows не вдасться.
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                // Поточна база лишається як .bak, доки відновлена не пройде міграції.
                File.Replace(tempPath, databasePath, previousPath);
            }
            else
            {
                File.Move(tempPath, databasePath);
            }

            try
            {
                // Бекап міг бути зроблений старішою версією застосунку.
                var migrated = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (migrated.ConfigureAwait(false))
                {
                    await migrated.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(previousPath))
                {
                    File.Move(previousPath, databasePath, overwrite: true);
                }

                throw new LocalMindException("Не вдалося оновити схему відновленої бази — повернуто попередню базу.", ex);
            }
        }
        finally
        {
            File.Delete(tempPath);
            File.Delete(previousPath);
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

                // Будь-яка валідна SQLite пройшла б integrity_check; порожня чи чужа база стерла б бібліотеку.
                command.CommandText = """
                    SELECT count(*) FROM sqlite_master
                    WHERE type = 'table' AND name IN ('__EFMigrationsHistory', 'Documents', 'SourceFolders')
                    """;
                if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not 3L)
                {
                    throw new LocalMindException("Бекап не є базою LocalMind — відновлення скасовано.");
                }
            }
        }
        catch (SqliteException ex)
        {
            throw new LocalMindException("Бекап не є базою LocalMind — відновлення скасовано.", ex);
        }
    }
}
