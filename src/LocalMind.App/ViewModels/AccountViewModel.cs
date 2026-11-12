using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;
using LocalMind.Core.Errors;
using Microsoft.Extensions.Logging;

namespace LocalMind.App.ViewModels;

/// <summary>
/// Обліковий запис на сервері бекапу: адреса сервера, реєстрація, вхід і вихід. Токени зберігає клієнт
/// (<see cref="IAccountClient"/>), тут лише стан сторінки і зрозумілі користувачу повідомлення.
/// Без адреси сервера сторінка пояснює, що сервер не налаштовано; решта застосунку працює далі.
/// </summary>
public sealed partial class AccountViewModel : ViewModelBase
{
    private const string NotConfiguredMessage = "Сервер не налаштовано. Вкажіть адресу сервера бекапу нижче.";
    private const string RestartMessage = "Адресу збережено. Перезапустіть застосунок, щоб підключитися до сервера.";
    private const string CredentialsRequiredMessage = "Вкажіть email і пароль.";
    private const string WrongCredentialsMessage = "Неправильний email або пароль.";
    private const string TooManyRequestsMessage = "Забагато спроб. Зачекайте кілька хвилин і спробуйте ще раз.";
    private const string SessionExpiredMessage = "Сесія завершилась. Увійдіть знову.";

    private readonly IAccountClient _client;
    private readonly IBackupServerSettings _server;
    private readonly ILogger<AccountViewModel> _logger;

    public AccountViewModel(IAccountClient client, IBackupServerSettings server, ILogger<AccountViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _server = server;
        _logger = logger;

        ServerUrl = server.SavedUrl;
        IsSignedIn = IsConfigured && client.IsSignedIn;
        UpdateServerState();
    }

    /// <summary>Адреса сервера в полі вводу.</summary>
    [ObservableProperty]
    public partial string ServerUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>Клієнт має адресу сервера з запуску; без неї реєстрація, вхід і вихід недоступні.</summary>
    public bool IsConfigured => _server.ActiveUrl is not null;

    /// <summary>Що з сервером: не налаштований, чекає перезапуску чи підключений до адреси.</summary>
    [ObservableProperty]
    public partial string ServerStatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CanLogin))]
    [NotifyPropertyChangedFor(nameof(CanLogout))]
    public partial bool IsSignedIn { get; private set; }

    public bool IsSignedOut => !IsSignedIn;

    /// <summary>Email користувача, що увійшов; невідомий, поки сервер не відповів.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? SignedInEmail { get; private set; }

    /// <summary>Йде запит до сервера; кнопки вимкнені, щоб не надіслати його двічі.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLogin))]
    [NotifyPropertyChangedFor(nameof(CanLogout))]
    [NotifyPropertyChangedFor(nameof(CanSaveServer))]
    public partial bool IsBusy { get; private set; }

    public bool CanLogin => IsConfigured && !IsSignedIn && !IsBusy;

    public bool CanLogout => IsConfigured && IsSignedIn && !IsBusy;

    public bool CanSaveServer => !IsBusy;

    /// <summary>«Ви увійшли як …» або «Ви не увійшли».</summary>
    public string StatusText => !IsSignedIn
        ? "Ви не увійшли."
        : SignedInEmail is { } email ? $"Ви увійшли як {email}." : "Ви увійшли.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo))]
    public partial string? InfoMessage { get; private set; }

    public bool HasInfo => InfoMessage is not null;

    /// <summary>Зберігає адресу сервера в налаштуваннях.</summary>
    [RelayCommand]
    private async Task SaveServerAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearMessages();
        var error = _server.Validate(ServerUrl);
        if (error is not null)
        {
            ErrorMessage = error;
            return;
        }

        IsBusy = true;
        try
        {
            await _server.SaveAsync(ServerUrl);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSaveServerFailed(_logger, exception);
            ErrorMessage = $"Не вдалося зберегти адресу сервера: {exception.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        ServerUrl = _server.SavedUrl;
        UpdateServerState();
        InfoMessage = _server.RestartRequired ? RestartMessage : "Адресу збережено.";
    }

    [RelayCommand]
    private Task RegisterAsync() => RunAsync(async () =>
    {
        if (!TryGetCredentials(out var email))
        {
            return;
        }

        await _client.RegisterAsync(email, Password);
        // Сервер відповідає однаково, чи був такий email уже зайнятий, тому більше за це не обіцяємо.
        InfoMessage = "Запит на реєстрацію прийнято. Тепер увійдіть із цим email і паролем.";
    }, WrongCredentialsMessage);

    [RelayCommand]
    private Task LoginAsync() => RunAsync(async () =>
    {
        if (!TryGetCredentials(out var email))
        {
            return;
        }

        await _client.LoginAsync(email, Password);
        SignedInEmail = email;
        IsSignedIn = true;
        Password = string.Empty;
    }, WrongCredentialsMessage);

    [RelayCommand]
    private Task LogoutAsync() => RunAsync(async () =>
    {
        await _client.LogoutAsync();
        SignIn(false);
        Password = string.Empty;
    }, SessionExpiredMessage);

    /// <summary>Показує актуальний стан, коли сторінку відкрито: чи є сесія і чий це обліковий запис.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsConfigured || IsBusy)
        {
            return;
        }

        if (!_client.IsSignedIn)
        {
            SignIn(false);
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            SignedInEmail = await _client.GetEmailAsync();
            IsSignedIn = true;
        }
        catch (BackupApiException exception)
        {
            // Сесію збережено, але email дізнатися не вдалося: лишаємо «увійшли» і пояснюємо причину.
            if (exception.IsSessionExpired)
            {
                SignIn(false);
                ErrorMessage = SessionExpiredMessage;
            }
            else
            {
                IsSignedIn = true;
                ErrorMessage = Describe(exception, SessionExpiredMessage);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Виконує дію сервера: вимикає кнопки на час запиту і перетворює помилки на повідомлення.
    /// <paramref name="unauthorizedMessage"/> — що означає 401 саме для цієї дії.
    /// </summary>
    private async Task RunAsync(Func<Task> action, string unauthorizedMessage)
    {
        if (IsBusy || !IsConfigured)
        {
            return;
        }

        ClearMessages();
        IsBusy = true;
        try
        {
            await action();
        }
        catch (BackupApiException exception)
        {
            ErrorMessage = Describe(exception, unauthorizedMessage);
            // Сесію відкликано чи вона сплила — клієнт уже стер токени.
            if (exception.IsSessionExpired && !_client.IsSignedIn)
            {
                SignIn(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogActionFailed(_logger, exception);
            ErrorMessage = $"Не вдалося виконати дію: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryGetCredentials(out string email)
    {
        email = Email.Trim();
        if (email.Length > 0 && Password.Length > 0)
        {
            return true;
        }

        ErrorMessage = CredentialsRequiredMessage;
        return false;
    }

    /// <summary>Помилка сервера мовою користувача; решту повідомлень клієнт уже формулює зрозуміло.</summary>
    private static string Describe(BackupApiException exception, string unauthorizedMessage) => exception.StatusCode switch
    {
        HttpStatusCode.Unauthorized => unauthorizedMessage,
        HttpStatusCode.TooManyRequests => TooManyRequestsMessage,
        _ => exception.Message,
    };

    private void SignIn(bool value)
    {
        IsSignedIn = value;
        if (!value)
        {
            SignedInEmail = null;
        }
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        InfoMessage = null;
    }

    private void UpdateServerState()
    {
        ServerStatusText = (IsConfigured, _server.RestartRequired) switch
        {
            (false, false) => NotConfiguredMessage,
            (_, true) => RestartMessage,
            _ => $"Сервер: {_server.ActiveUrl}",
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося зберегти адресу сервера бекапу")]
    private static partial void LogSaveServerFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Несподівана помилка облікового запису")]
    private static partial void LogActionFailed(ILogger logger, Exception exception);
}
