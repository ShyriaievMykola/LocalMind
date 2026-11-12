using System.Text.Json.Nodes;
using LocalMind.Core.Backup;
using Microsoft.Extensions.Options;

namespace LocalMind.App.Services;

/// <summary>
/// Зберігає адресу сервера бекапу в тому самому файлі, що й решта налаштувань, у секції <c>LocalMind:Backup</c>.
/// <see cref="AppHost"/> додає файл до конфігурації, тож після перезапуску адресу отримує <see cref="BackupClientOptions"/>.
/// </summary>
internal sealed class BackupServerSettings : IBackupServerSettings
{
    private readonly UserSettingsFile _file;

    public BackupServerSettings(IOptions<BackupClientOptions> options, UserSettingsFile file)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(file);

        _file = file;
        ActiveUrl = ReadUrl(options);
        SavedUrl = ActiveUrl?.ToString() ?? string.Empty;
    }

    public Uri? ActiveUrl { get; }

    public string SavedUrl { get; private set; }

    public bool RestartRequired => !string.Equals(SavedUrl, ActiveUrl?.ToString() ?? string.Empty, StringComparison.Ordinal);

    public string? Validate(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return "Адреса сервера має бути повною, наприклад https://backup.example.com.";
        }

        // Логін і пароль в адресі лягли б у settings.json відкритим текстом.
        if (uri.UserInfo.Length > 0)
        {
            return "Адреса не повинна містити логін чи пароль.";
        }

        // Те саме правило, що й під час запуску: HTTP лише для localhost, решта — HTTPS.
        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            ? null
            : "Адреса має починатися з https://. http:// дозволено лише для localhost.";
    }

    public async Task SaveAsync(string url, CancellationToken cancellationToken = default)
    {
        var error = Validate(url);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(url));
        }

        var normalized = string.IsNullOrWhiteSpace(url) ? string.Empty : new Uri(url.Trim()).ToString();

        // Лише ключ адреси: решту секції (наприклад, TokenFilePath, якщо його задали вручну) не чіпаємо.
        await UserSettingsJson.UpdateAsync(
            _file.Path,
            root => UserSettingsJson.Section(root, BackupClientOptions.SectionName)["ServerUrl"] = JsonValue.Create(normalized),
            cancellationToken);
        SavedUrl = normalized;
    }

    private static Uri? ReadUrl(IOptions<BackupClientOptions> options)
    {
        try
        {
            return options.Value.ServerUrl;
        }
        catch (OptionsValidationException)
        {
            // Адреса в конфігурації некоректна: ядро вже повідомило про це, сторінка все одно має відкритися.
            return null;
        }
    }
}
