namespace LocalMind.App.Services;

/// <summary>Обліковий запис на сервері бекапу: реєстрація, вхід і вихід.</summary>
public interface IAccountClient
{
    /// <summary>Чи є збережена сесія.</summary>
    bool IsSignedIn { get; }

    /// <summary>Реєструє обліковий запис (без входу).</summary>
    /// <exception cref="LocalMind.Core.Errors.BackupApiException">Слабкий пароль, забагато спроб, сервер недоступний.</exception>
    Task RegisterAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Входить; токени зберігає клієнт.</summary>
    /// <exception cref="LocalMind.Core.Errors.BackupApiException">Неправильний email або пароль, забагато спроб, сервер недоступний.</exception>
    Task LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Виходить: сесію стирають локально, а на сервері відкликають, якщо він доступний.</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Email поточного користувача.</summary>
    /// <exception cref="LocalMind.Core.Errors.BackupApiException">Сесія завершилась, сервер недоступний.</exception>
    Task<string> GetEmailAsync(CancellationToken cancellationToken = default);
}
