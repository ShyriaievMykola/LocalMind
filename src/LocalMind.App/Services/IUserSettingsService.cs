namespace LocalMind.App.Services;

/// <summary>Читання, перевірка і збереження налаштувань користувача.</summary>
public interface IUserSettingsService
{
    /// <summary>Налаштування, з якими застосунок працює зараз (прочитані під час запуску).</summary>
    UserSettings Current { get; }

    /// <summary>Перевіряє налаштування тими самими правилами, що й під час запуску.</summary>
    /// <returns>Повідомлення про помилки; порожній список — усе гаразд.</returns>
    IReadOnlyList<string> Validate(UserSettings settings);

    /// <summary>Зберігає налаштування у файл; вони почнуть діяти після перезапуску.</summary>
    /// <exception cref="ArgumentException">Налаштування некоректні (див. <see cref="Validate"/>).</exception>
    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
}