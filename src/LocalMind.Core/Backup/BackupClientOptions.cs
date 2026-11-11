namespace LocalMind.Core.Backup;

/// <summary>Налаштування клієнта бекапу. Секція конфігурації — <see cref="SectionName"/>.</summary>
public sealed class BackupClientOptions
{
    /// <summary>Секція конфігурації.</summary>
    public const string SectionName = "LocalMind:Backup";

    /// <summary>Адреса сервера бекапу, напр. <c>https://backup.example.com</c>. Поза localhost — лише HTTPS.</summary>
    public Uri? ServerUrl { get; set; }

    /// <summary>
    /// Файл із сесією (refresh-токеном), зашифрований DPAPI. Використовується лише на Windows;
    /// якщо порожній — <c>session.bin</c> поруч зі стандартною базою.
    /// </summary>
    public string TokenFilePath { get; set; } = string.Empty;

    /// <summary>Стандартний шлях до файла сесії.</summary>
    public static string DefaultTokenFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "LocalMind",
            "session.bin");

    /// <summary>
    /// Додає «/» у кінець шляху: інакше відносне <c>api/v1/...</c> від <c>https://host/localmind</c> замінило б
    /// останній сегмент, і префікс загубився б.
    /// </summary>
    internal static Uri? WithTrailingSlash(Uri? url) =>
        url is null || url.AbsolutePath.EndsWith('/') ? url : new UriBuilder(url) { Path = url.AbsolutePath + "/" }.Uri;

    /// <summary>Перевірка адреси: абсолютна, HTTPS (HTTP дозволено лише для localhost).</summary>
    internal static bool IsValidServerUrl(Uri? url) =>
        url is { IsAbsoluteUri: true }
        && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback));
}
