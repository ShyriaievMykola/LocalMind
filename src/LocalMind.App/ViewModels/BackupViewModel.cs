using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;
using LocalMind.Core.Errors;
using Microsoft.Extensions.Logging;

namespace LocalMind.App.ViewModels;

/// <summary>
/// Бекапи на сервері: створити (пароль шифрування двічі), список знімків з видаленням, відновлення з підтвердженням
/// і паролем. Довга операція показує стан і скасовується. Після відновлення потрібен перезапуск: пошуковий індекс
/// і бібліотека в пам'яті лишаються від старої бази.
/// </summary>
public sealed partial class BackupViewModel : ViewModelBase
{
    /// <summary>Попередження біля поля пароля: без пароля бекап не розшифрувати ні нам, ні серверу.</summary>
    public const string PasswordWarning =
        "Забудете пароль — бекап не вдасться відновити: сервер зберігає лише зашифровані дані й пароля не знає.";

    private const string NotConfiguredMessage =
        "Сервер не налаштовано. Вкажіть його адресу на сторінці «Обліковий запис».";

    private const string NotSignedInMessage = "Увійдіть на сторінці «Обліковий запис», щоб працювати з бекапами.";
    private const string RestartMessage = "Бібліотеку відновлено з бекапу. Перезапустіть застосунок, щоб оновити пошук і бібліотеку.";
    private const string SessionExpiredMessage = "Сесія завершилась. Увійдіть знову на сторінці «Обліковий запис».";
    private const string TooManyRequestsMessage = "Забагато запитів. Зачекайте кілька хвилин і спробуйте ще раз.";
    private const string PasswordRequiredMessage = "Вкажіть пароль шифрування.";
    private const string PasswordsDifferMessage = "Паролі не збігаються.";

    private const string WrongPasswordMessage =
        "Неправильний пароль або бекап пошкоджено. Поточна бібліотека не змінена.";

    private readonly IBackupOperations _backups;
    private readonly IAccountClient _account;
    private readonly IBackupServerSettings _server;
    private readonly ILogger<BackupViewModel> _logger;
    private CancellationTokenSource? _operation;

    public BackupViewModel(
        IBackupOperations backups,
        IAccountClient account,
        IBackupServerSettings server,
        ILogger<BackupViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(logger);

        _backups = backups;
        _account = account;
        _server = server;
        _logger = logger;
        IsSignedIn = IsConfigured && account.IsSignedIn;
    }

    /// <summary>Клієнт має адресу сервера з запуску.</summary>
    public bool IsConfigured => _server.ActiveUrl is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    [NotifyPropertyChangedFor(nameof(AvailabilityText))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    public partial bool IsSignedIn { get; private set; }

    /// <summary>База замінена бекапом: до перезапуску нові операції вимкнені.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    [NotifyPropertyChangedFor(nameof(AvailabilityText))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    public partial bool IsRestartRequired { get; private set; }

    /// <summary>Можна працювати з бекапами: сервер налаштовано, користувач увійшов, база не чекає перезапуску.</summary>
    public bool IsAvailable => IsConfigured && IsSignedIn && !IsRestartRequired;

    /// <summary>Чому сторінка недоступна; <see langword="null"/>, коли доступна.</summary>
    public string? AvailabilityText => !IsConfigured ? NotConfiguredMessage
        : IsRestartRequired ? RestartMessage
        : !IsSignedIn ? NotSignedInMessage
        : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackups))]
    public partial IReadOnlyList<BackupItem> Backups { get; private set; } = [];

    public bool HasBackups => Backups.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    public partial BackupItem? SelectedBackup { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PasswordRepeat { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RestorePassword { get; set; } = string.Empty;

    /// <summary>Тривалий запит до сервера: кнопки вимкнені, показано стан і «Скасувати».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? BusyText { get; private set; }

    public bool CanCreate => IsAvailable && !IsBusy;

    public bool CanManage => IsAvailable && !IsBusy && SelectedBackup is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingRestore))]
    public partial BackupItem? RestoreTarget { get; private set; }

    public bool IsConfirmingRestore => RestoreTarget is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingDelete))]
    public partial BackupItem? DeleteTarget { get; private set; }

    public bool IsConfirmingDelete => DeleteTarget is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo))]
    public partial string? InfoMessage { get; private set; }

    public bool HasInfo => InfoMessage is not null;

    /// <summary>Показує актуальний стан, коли сторінку відкрито: чи є сесія і які є бекапи.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsSignedIn = IsConfigured && _account.IsSignedIn;
        if (IsAvailable)
        {
            await RunAsync("Завантажую список бекапів…", LoadListAsync);
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (!CanCreate)
        {
            return;
        }

        ClearMessages();
        if (Password.Length == 0)
        {
            ErrorMessage = PasswordRequiredMessage;
            return;
        }

        if (!string.Equals(Password, PasswordRepeat, StringComparison.Ordinal))
        {
            ErrorMessage = PasswordsDifferMessage;
            return;
        }

        var password = Password;
        await RunAsync("Шифрую й завантажую бекап…", async token =>
        {
            var created = new BackupItem(await _backups.CreateAsync(password, token));
            // Бекап уже на сервері: пароль більше не потрібен, навіть якщо оновити список не вдасться.
            Password = string.Empty;
            PasswordRepeat = string.Empty;
            InfoMessage = $"Бекап створено: {created.CreatedText}, {created.SizeText}.";
            await LoadListAsync(token);
        });
    }

    [RelayCommand]
    private void RequestDelete()
    {
        if (CanManage)
        {
            RestoreTarget = null;
            DeleteTarget = SelectedBackup;
        }
    }

    [RelayCommand]
    private void CancelDelete() => DeleteTarget = null;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (DeleteTarget is not { } target || IsBusy)
        {
            return;
        }

        DeleteTarget = null;
        await RunAsync("Видаляю бекап…", async token =>
        {
            await _backups.DeleteAsync(target.Id, token);
            InfoMessage = "Бекап видалено.";
            await LoadListAsync(token);
        });
    }

    [RelayCommand]
    private void RequestRestore()
    {
        if (CanManage)
        {
            DeleteTarget = null;
            RestorePassword = string.Empty;
            RestoreTarget = SelectedBackup;
        }
    }

    [RelayCommand]
    private void CancelRestore()
    {
        RestoreTarget = null;
        RestorePassword = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmRestoreAsync()
    {
        if (RestoreTarget is not { } target || IsBusy)
        {
            return;
        }

        ClearMessages();
        if (RestorePassword.Length == 0)
        {
            ErrorMessage = PasswordRequiredMessage;
            return;
        }

        var password = RestorePassword;
        var restored = await RunAsync("Завантажую й відновлюю бекап…", token => _backups.RestoreAsync(target.Id, password, token));
        RestorePassword = string.Empty;
        if (restored)
        {
            RestoreTarget = null;
            InfoMessage = $"Відновлено бекап від {target.CreatedText}.";
            IsRestartRequired = true;
        }
    }

    /// <summary>Скасовує поточну довгу операцію; уже зроблене на сервері чи в базі не відкочується, окрім заміни бази.</summary>
    [RelayCommand]
    private void CancelOperation() => _operation?.Cancel();

    private async Task LoadListAsync(CancellationToken cancellationToken)
    {
        var items = await _backups.ListAsync(cancellationToken);
        Backups = [.. items.OrderByDescending(info => info.CreatedAt).Select(info => new BackupItem(info))];
        SelectedBackup = null;
    }

    /// <summary>
    /// Виконує дію, поки показано <paramref name="busyText"/> і працює «Скасувати», та перетворює помилки на повідомлення.
    /// </summary>
    /// <returns><see langword="true"/>, якщо дія завершилась без помилки і не була скасована.</returns>
    private async Task<bool> RunAsync(string busyText, Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return false;
        }

        ClearMessages();
        BusyText = busyText;
        IsBusy = true;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        try
        {
            await action(operation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            InfoMessage = "Операцію скасовано.";
        }
        catch (BackupDecryptionException)
        {
            ErrorMessage = WrongPasswordMessage;
        }
        catch (BackupApiException exception)
        {
            ErrorMessage = Describe(exception);
            if (exception.IsSessionExpired)
            {
                IsSignedIn = false;
            }
        }
        catch (LocalMindException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (Exception exception)
        {
            LogOperationFailed(_logger, exception);
            ErrorMessage = $"Не вдалося виконати дію: {exception.Message}";
        }
        finally
        {
            _operation = null;
            IsBusy = false;
            BusyText = null;
        }

        return false;
    }

    private static string Describe(BackupApiException exception) => exception.StatusCode switch
    {
        HttpStatusCode.Unauthorized => SessionExpiredMessage,
        HttpStatusCode.TooManyRequests => TooManyRequestsMessage,
        _ => exception.Message,
    };

    private void ClearMessages()
    {
        ErrorMessage = null;
        InfoMessage = null;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Несподівана помилка під час роботи з бекапами")]
    private static partial void LogOperationFailed(ILogger logger, Exception exception);
}
