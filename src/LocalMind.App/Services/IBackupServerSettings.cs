namespace LocalMind.App.Services;

/// <summary>
/// Адреса сервера бекапу в налаштуваннях користувача. Клієнт бекапу читає її під час запуску,
/// тож нова адреса починає діяти після перезапуску застосунку.
/// </summary>
public interface IBackupServerSettings
{
    /// <summary>Адреса, з якою клієнт працює зараз (прочитана під час запуску); <see langword="null"/>, якщо сервер не налаштовано.</summary>
    Uri? ActiveUrl { get; }

    /// <summary>Адреса, збережена в налаштуваннях; порожній рядок, якщо її немає.</summary>
    string SavedUrl { get; }

    /// <summary>Збережена адреса відрізняється від тієї, з якою клієнт працює зараз.</summary>
    bool RestartRequired { get; }

    /// <summary>Перевіряє адресу; порожня дозволена — вона вимикає сервер.</summary>
    /// <returns>Повідомлення про помилку або <see langword="null"/>, якщо все гаразд.</returns>
    string? Validate(string url);

    /// <summary>Зберігає адресу у файл налаштувань; порожній рядок прибирає її.</summary>
    /// <exception cref="ArgumentException">Адреса некоректна (див. <see cref="Validate"/>).</exception>
    Task SaveAsync(string url, CancellationToken cancellationToken = default);
}
