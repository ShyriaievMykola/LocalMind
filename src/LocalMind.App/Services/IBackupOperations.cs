using LocalMind.Core.Backup.Dtos;

namespace LocalMind.App.Services;

/// <summary>Бекапи на сервері: створити, переглянути, видалити, відновити.</summary>
public interface IBackupOperations
{
    /// <summary>Шифрує поточну базу паролем і завантажує на сервер.</summary>
    /// <exception cref="LocalMind.Core.Errors.BackupApiException">Бекап завеликий, сесія завершилась, сервер недоступний.</exception>
    Task<BackupInfo> CreateAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>Бекапи користувача, новіші першими.</summary>
    Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Видаляє бекап із сервера.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Зупиняє індексацію, завантажує бекап і замінює ним базу. Будь-яка помилка до заміни лишає поточну базу без змін.
    /// Вектори пошуку й бібліотека оновляться після перезапуску застосунку.
    /// </summary>
    /// <exception cref="LocalMind.Core.Errors.BackupDecryptionException">Неправильний пароль або бекап пошкоджено.</exception>
    Task RestoreAsync(Guid id, string password, CancellationToken cancellationToken = default);
}
