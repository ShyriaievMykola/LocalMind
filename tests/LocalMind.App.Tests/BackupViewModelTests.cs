using System.Net;
using LocalMind.App.ViewModels;
using LocalMind.Core.Errors;

namespace LocalMind.App.Tests;

public sealed class BackupViewModelTests
{
    private static readonly DateTimeOffset Older = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Newer = new(2026, 11, 10, 12, 30, 0, TimeSpan.Zero);

    private readonly FakeBackupOperations _backups = new();
    private readonly FakeAccountClient _account = new() { IsSignedIn = true };
    private readonly FakeBackupServerSettings _server = new();

    [Fact]
    public async Task Refresh_NoServer_ExplainsAndDoesNotCallServer()
    {
        _server.ActiveUrl = null;
        _server.SavedUrl = string.Empty;
        var viewModel = _backups.CreateViewModel(_account, _server);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsAvailable);
        Assert.Contains("не налаштовано", viewModel.AvailabilityText, StringComparison.Ordinal);
        Assert.Equal(0, _backups.ListCalls);
    }

    [Fact]
    public async Task Refresh_NotSignedIn_AsksToSignIn()
    {
        _account.IsSignedIn = false;
        var viewModel = _backups.CreateViewModel(_account, _server);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsAvailable);
        Assert.Contains("Увійдіть", viewModel.AvailabilityText, StringComparison.Ordinal);
        Assert.Equal(0, _backups.ListCalls);
        Assert.False(viewModel.CanCreate);
    }

    [Fact]
    public async Task Refresh_SignedIn_ListsBackupsNewestFirst()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Older));
        _backups.Server.Add(FakeBackupOperations.Backup(3 * 1024 * 1024, Newer));
        var viewModel = _backups.CreateViewModel(_account, _server);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsAvailable);
        Assert.Null(viewModel.AvailabilityText);
        Assert.True(viewModel.HasBackups);
        Assert.Equal([Newer, Older], viewModel.Backups.Select(item => item.Info.CreatedAt));
        Assert.Equal("3 МБ", viewModel.Backups[0].SizeText);
    }

    [Fact]
    public async Task Refresh_ServerUnavailable_ShowsError()
    {
        _backups.Error = new BackupApiException(null, "Сервер бекапу недоступний. Перевірте підключення й адресу сервера.");
        var viewModel = _backups.CreateViewModel(_account, _server);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("недоступний", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(viewModel.HasBackups);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Create_Success_UploadsClearsPasswordsAndRefreshesList()
    {
        var viewModel = _backups.CreateViewModel(_account, _server);
        viewModel.Password = "довгий-пароль-шифрування";
        viewModel.PasswordRepeat = "довгий-пароль-шифрування";

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Equal(["довгий-пароль-шифрування"], _backups.CreatedWith);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Equal(string.Empty, viewModel.PasswordRepeat);
        Assert.Contains("Бекап створено", viewModel.InfoMessage, StringComparison.Ordinal);
        Assert.Single(viewModel.Backups);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Create_EmptyPassword_AsksForItWithoutCallingServer()
    {
        var viewModel = _backups.CreateViewModel(_account, _server);

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Equal("Вкажіть пароль шифрування.", viewModel.ErrorMessage);
        Assert.Empty(_backups.CreatedWith);
    }

    [Fact]
    public async Task Create_PasswordsDiffer_ShowsErrorWithoutCallingServer()
    {
        var viewModel = _backups.CreateViewModel(_account, _server);
        viewModel.Password = "довгий-пароль-шифрування";
        viewModel.PasswordRepeat = "інший-пароль-шифрування";

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Equal("Паролі не збігаються.", viewModel.ErrorMessage);
        Assert.Empty(_backups.CreatedWith);
    }

    [Fact]
    public void PasswordWarning_SaysBackupCannotBeRestoredWithoutPassword() =>
        Assert.Contains("не вдасться відновити", BackupViewModel.PasswordWarning, StringComparison.Ordinal);

    [Fact]
    public async Task Create_ServerUnavailable_ShowsErrorAndKeepsPasswords()
    {
        _backups.Error = new BackupApiException(null, "Сервер бекапу недоступний. Перевірте підключення й адресу сервера.");
        var viewModel = _backups.CreateViewModel(_account, _server);
        viewModel.Password = "довгий-пароль-шифрування";
        viewModel.PasswordRepeat = "довгий-пароль-шифрування";

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Contains("недоступний", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("довгий-пароль-шифрування", viewModel.Password);
    }

    [Fact]
    public async Task Create_SessionExpired_ShowsSignInRequired()
    {
        _backups.Error = new BackupApiException(HttpStatusCode.Unauthorized, "Сесія завершилась. Увійдіть знову.");
        var viewModel = _backups.CreateViewModel(_account, _server);
        viewModel.Password = "довгий-пароль-шифрування";
        viewModel.PasswordRepeat = "довгий-пароль-шифрування";

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Contains("Сесія завершилась", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsAvailable);
        Assert.Contains("Увійдіть", viewModel.AvailabilityText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOut_ClearsPasswordsAndOpenConfirmations()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();
        viewModel.Password = "довгий-пароль-шифрування";
        viewModel.PasswordRepeat = "довгий-пароль-шифрування";
        viewModel.SelectedBackup = viewModel.Backups[0];
        viewModel.RequestRestoreCommand.Execute(null);
        viewModel.RestorePassword = "довгий-пароль-шифрування";

        _account.IsSignedIn = false;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSignedIn);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Equal(string.Empty, viewModel.PasswordRepeat);
        Assert.Equal(string.Empty, viewModel.RestorePassword);
        Assert.False(viewModel.IsConfirmingRestore);
    }

    [Fact]
    public async Task SessionExpired_ClearsPasswords()
    {
        _backups.Error = new BackupApiException(HttpStatusCode.Unauthorized, "Сесія завершилась. Увійдіть знову.");
        var viewModel = _backups.CreateViewModel(_account, _server);
        viewModel.Password = "довгий-пароль-шифрування";
        viewModel.PasswordRepeat = "довгий-пароль-шифрування";

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Equal(string.Empty, viewModel.PasswordRepeat);
    }

    [Fact]
    public void Constructor_RestartAlreadyRequired_KeepsPageUnavailable()
    {
        _backups.Restart.Require();

        var viewModel = _backups.CreateViewModel(_account, _server);

        Assert.True(viewModel.IsRestartRequired);
        Assert.False(viewModel.IsAvailable);
    }

    [Fact]
    public async Task Create_TooLarge_ShowsServerMessage()
    {
        _backups.Error = new BackupApiException(HttpStatusCode.RequestEntityTooLarge, "Бекап завеликий для сервера.");
        var viewModel = _backups.CreateViewModel(_account, _server);
        viewModel.Password = "довгий-пароль-шифрування";
        viewModel.PasswordRepeat = "довгий-пароль-шифрування";

        await viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Equal("Бекап завеликий для сервера.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Delete_NeedsConfirmation_ThenRemovesBackup()
    {
        var backup = FakeBackupOperations.Backup(1024, Newer);
        _backups.Server.Add(backup);
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups[0];

        viewModel.RequestDeleteCommand.Execute(null);
        Assert.True(viewModel.IsConfirmingDelete);
        Assert.Empty(_backups.Deleted);

        await viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal([backup.Id], _backups.Deleted);
        Assert.False(viewModel.IsConfirmingDelete);
        Assert.False(viewModel.HasBackups);
        Assert.Equal("Бекап видалено.", viewModel.InfoMessage);
    }

    [Fact]
    public async Task Delete_Cancelled_KeepsBackup()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups[0];
        viewModel.RequestDeleteCommand.Execute(null);

        viewModel.CancelDeleteCommand.Execute(null);

        Assert.False(viewModel.IsConfirmingDelete);
        Assert.Empty(_backups.Deleted);
        Assert.True(viewModel.HasBackups);
    }

    [Fact]
    public async Task Restore_NothingSelected_DoesNotAskForConfirmation()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();

        viewModel.RequestRestoreCommand.Execute(null);

        Assert.False(viewModel.IsConfirmingRestore);
    }

    [Fact]
    public async Task Restore_Success_ReplacesDatabaseAndAsksForRestart()
    {
        var backup = FakeBackupOperations.Backup(1024, Newer);
        _backups.Server.Add(backup);
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups[0];
        viewModel.RequestRestoreCommand.Execute(null);
        Assert.True(viewModel.IsConfirmingRestore);
        Assert.Empty(_backups.Restored);
        viewModel.RestorePassword = "довгий-пароль-шифрування";

        await viewModel.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.Equal([(backup.Id, "довгий-пароль-шифрування")], _backups.Restored);
        Assert.False(viewModel.IsConfirmingRestore);
        Assert.Equal(string.Empty, viewModel.RestorePassword);
        Assert.True(viewModel.IsRestartRequired);
        Assert.Contains("перезапуск", viewModel.AvailabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.True(_backups.Restart.IsRequired);
        Assert.False(viewModel.CanCreate);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Restore_WrongPassword_ExplainsAndKeepsCurrentLibrary()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups[0];
        viewModel.RequestRestoreCommand.Execute(null);
        viewModel.RestorePassword = "не-той-пароль";
        _backups.Error = new BackupDecryptionException();

        await viewModel.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.Contains("Неправильний пароль", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("не змінена", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsRestartRequired);
        Assert.True(viewModel.IsAvailable);
        // Підтвердження лишається: можна ввести пароль ще раз.
        Assert.True(viewModel.IsConfirmingRestore);
        Assert.Equal(string.Empty, viewModel.RestorePassword);
    }

    [Fact]
    public async Task Restore_DamagedBackup_ShowsServiceMessage()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups[0];
        viewModel.RequestRestoreCommand.Execute(null);
        viewModel.RestorePassword = "довгий-пароль-шифрування";
        _backups.Error = new LocalMindException("Бекап не є базою LocalMind — відновлення скасовано.");

        await viewModel.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.Equal("Бекап не є базою LocalMind — відновлення скасовано.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsRestartRequired);
    }

    [Fact]
    public async Task Restore_EmptyPassword_AsksForItWithoutCallingServer()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups[0];
        viewModel.RequestRestoreCommand.Execute(null);

        await viewModel.ConfirmRestoreCommand.ExecuteAsync(null);

        Assert.Equal("Вкажіть пароль шифрування.", viewModel.ErrorMessage);
        Assert.Empty(_backups.Restored);
        Assert.True(viewModel.IsConfirmingRestore);
    }

    [Fact]
    public async Task Restore_Cancelled_ByUser_BeforeConfirmation_DoesNothing()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups[0];
        viewModel.RequestRestoreCommand.Execute(null);
        viewModel.RestorePassword = "довгий-пароль-шифрування";

        viewModel.CancelRestoreCommand.Execute(null);

        Assert.False(viewModel.IsConfirmingRestore);
        Assert.Equal(string.Empty, viewModel.RestorePassword);
        Assert.Empty(_backups.Restored);
    }

    [Fact]
    public async Task CancelOperation_DuringLongOperation_StopsItAndKeepsPasswords()
    {
        _backups.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = _backups.CreateViewModel(_account, _server);
        viewModel.Password = "довгий-пароль-шифрування";
        viewModel.PasswordRepeat = "довгий-пароль-шифрування";

        var pending = viewModel.CreateBackupCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);
        Assert.NotNull(viewModel.BusyText);
        Assert.False(viewModel.CanCreate);

        viewModel.CancelOperationCommand.Execute(null);
        await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.BusyText);
        Assert.Contains("скасовано", viewModel.InfoMessage, StringComparison.Ordinal);
        Assert.Contains("перевірте список", viewModel.InfoMessage, StringComparison.Ordinal);
        Assert.False(viewModel.HasError);
        Assert.Empty(_backups.CreatedWith);
        Assert.Equal("довгий-пароль-шифрування", viewModel.Password);
    }

    [Fact]
    public async Task CancelOperation_AfterOperationFinished_DoesNothing()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();

        viewModel.CancelOperationCommand.Execute(null);

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.HasInfo);
    }

    [Fact]
    public async Task Refresh_KeepsSelectedBackupWhileItExists()
    {
        var kept = FakeBackupOperations.Backup(1024, Older);
        _backups.Server.Add(kept);
        _backups.Server.Add(FakeBackupOperations.Backup(2048, Newer));
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups.Single(item => item.Id == kept.Id);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(kept.Id, viewModel.SelectedBackup?.Id);
    }

    [Fact]
    public async Task Delete_Cancelled_KeepsSelectionAfterRefresh()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups[0];
        viewModel.RequestDeleteCommand.Execute(null);
        viewModel.CancelDeleteCommand.Execute(null);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SelectedBackup);
        Assert.True(viewModel.CanManage);
    }

    [Fact]
    public async Task Delete_Success_ClearsSelectionOfRemovedBackup()
    {
        _backups.Server.Add(FakeBackupOperations.Backup(1024, Newer));
        var viewModel = await OpenAsync();
        viewModel.SelectedBackup = viewModel.Backups[0];
        viewModel.RequestDeleteCommand.Execute(null);

        await viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedBackup);
        Assert.False(viewModel.CanManage);
    }

    [Theory]
    [InlineData(512, "512 Б")]
    [InlineData(1536, "1.5 КБ")]
    [InlineData(5 * 1024 * 1024, "5 МБ")]
    public void FormatSize_UsesReadableUnits(long bytes, string expected) =>
        Assert.Equal(expected, BackupItem.FormatSize(bytes));

    private async Task<BackupViewModel> OpenAsync()
    {
        var viewModel = _backups.CreateViewModel(_account, _server);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }
}
