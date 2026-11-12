using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

/// <summary>Клієнт облікового запису без мережі: запам'ятовує виклики, помилку задає тест.</summary>
internal sealed class FakeAccountClient : IAccountClient
{
    public bool IsSignedIn { get; set; }

    /// <summary>Email, який «сервер» повертає для поточного користувача.</summary>
    public string CurrentEmail { get; set; } = "user@example.com";

    /// <summary>Кидається під час реєстрації, входу й запиту email; вихід помилок не кидає, як і справжній клієнт.</summary>
    public Exception? Error { get; set; }

    public List<(string Email, string Password)> Registrations { get; } = [];

    public List<(string Email, string Password)> Logins { get; } = [];

    public int Logouts { get; private set; }

    public Task RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (Error is not null)
        {
            return Task.FromException(Error);
        }

        Registrations.Add((email, password));
        return Task.CompletedTask;
    }

    public Task LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (Error is not null)
        {
            return Task.FromException(Error);
        }

        Logins.Add((email, password));
        IsSignedIn = true;
        return Task.CompletedTask;
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        Logouts++;
        IsSignedIn = false;
        return Task.CompletedTask;
    }

    public Task<string> GetEmailAsync(CancellationToken cancellationToken = default) =>
        Error is not null ? Task.FromException<string>(Error) : Task.FromResult(CurrentEmail);

    public AccountViewModel CreateViewModel(IBackupServerSettings? server = null) =>
        new(this, server ?? new FakeBackupServerSettings(), NullLogger<AccountViewModel>.Instance);
}

/// <summary>Адреса сервера без файла: за замовчуванням сервер налаштовано і перезапуск не потрібен.</summary>
internal sealed class FakeBackupServerSettings : IBackupServerSettings
{
    public Uri? ActiveUrl { get; set; } = new("http://localhost:5133/");

    public string SavedUrl { get; set; } = "http://localhost:5133/";

    public bool RestartRequired => SavedUrl != (ActiveUrl?.ToString() ?? string.Empty);

    public string? ValidationError { get; set; }

    public Exception? SaveError { get; set; }

    public List<string> Saved { get; } = [];

    public string? Validate(string url) => ValidationError;

    public Task SaveAsync(string url, CancellationToken cancellationToken = default)
    {
        if (SaveError is not null)
        {
            return Task.FromException(SaveError);
        }

        Saved.Add(url);
        SavedUrl = url;
        return Task.CompletedTask;
    }
}
