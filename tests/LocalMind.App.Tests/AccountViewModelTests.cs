using System.Net;
using LocalMind.Core.Errors;

namespace LocalMind.App.Tests;

public sealed class AccountViewModelTests
{
    private readonly FakeAccountClient _client = new();
    private readonly FakeBackupServerSettings _server = new();

    [Fact]
    public void Constructor_NoServer_ShowsNotConfiguredAndDisablesActions()
    {
        _server.ActiveUrl = null;
        _server.SavedUrl = string.Empty;

        var viewModel = _client.CreateViewModel(_server);

        Assert.False(viewModel.IsConfigured);
        Assert.Contains("не налаштовано", viewModel.ServerStatusText, StringComparison.Ordinal);
        Assert.False(viewModel.CanLogin);
        Assert.False(viewModel.CanLogout);
        Assert.True(viewModel.CanSaveServer);
        Assert.False(viewModel.IsSignedIn);
    }

    [Fact]
    public async Task Login_NoServer_DoesNotCallClient()
    {
        _server.ActiveUrl = null;
        _server.SavedUrl = string.Empty;
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Email = "user@example.com";
        viewModel.Password = "дуже-довгий-пароль";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Empty(_client.Logins);
        Assert.False(viewModel.IsSignedIn);
    }

    [Fact]
    public void Constructor_ServerConfigured_ShowsAddressAndSignedOut()
    {
        var viewModel = _client.CreateViewModel(_server);

        Assert.True(viewModel.IsConfigured);
        Assert.Equal("http://localhost:5133/", viewModel.ServerUrl);
        Assert.Contains("http://localhost:5133/", viewModel.ServerStatusText, StringComparison.Ordinal);
        Assert.False(viewModel.IsSignedIn);
        Assert.Equal("Ви не увійшли.", viewModel.StatusText);
        Assert.True(viewModel.CanLogin);
    }

    [Fact]
    public async Task Login_Success_ShowsSignedInEmailAndClearsPassword()
    {
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Email = "  user@example.com ";
        viewModel.Password = "дуже-довгий-пароль";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal([("user@example.com", "дуже-довгий-пароль")], _client.Logins);
        Assert.True(viewModel.IsSignedIn);
        Assert.Equal("Ви увійшли як user@example.com.", viewModel.StatusText);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.CanLogin);
        Assert.True(viewModel.CanLogout);
    }

    [Fact]
    public async Task Login_WrongPassword_ShowsClearError()
    {
        _client.Error = new BackupApiException(HttpStatusCode.Unauthorized, "Неправильний email або пароль.");
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Email = "user@example.com";
        viewModel.Password = "не-той-пароль";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSignedIn);
        Assert.Equal("Неправильний email або пароль.", viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Login_ServerUnavailable_ShowsClientMessage()
    {
        _client.Error = new BackupApiException(null, "Сервер бекапу недоступний. Перевірте підключення й адресу сервера.");
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Email = "user@example.com";
        viewModel.Password = "дуже-довгий-пароль";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSignedIn);
        Assert.Contains("недоступний", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Login_TooManyRequests_AsksToWait()
    {
        _client.Error = new BackupApiException(HttpStatusCode.TooManyRequests, "Забагато спроб. Спробуйте трохи пізніше.");
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Email = "user@example.com";
        viewModel.Password = "дуже-довгий-пароль";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSignedIn);
        Assert.Contains("Забагато спроб", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Зачекайте", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_UnexpectedException_ShowsMessageWithoutThrowing()
    {
        _client.Error = new InvalidOperationException("щось зламалось");
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Email = "user@example.com";
        viewModel.Password = "дуже-довгий-пароль";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Contains("щось зламалось", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData("", "дуже-довгий-пароль")]
    [InlineData("user@example.com", "")]
    [InlineData("   ", "дуже-довгий-пароль")]
    public async Task LoginAndRegister_EmptyCredentials_AskForThemWithoutCallingClient(string email, string password)
    {
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Email = email;
        viewModel.Password = password;

        await viewModel.LoginCommand.ExecuteAsync(null);
        var loginError = viewModel.ErrorMessage;
        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Вкажіть email і пароль.", loginError);
        Assert.Equal("Вкажіть email і пароль.", viewModel.ErrorMessage);
        Assert.Empty(_client.Logins);
        Assert.Empty(_client.Registrations);
    }

    [Fact]
    public async Task Register_Success_AsksToSignInAndStaysSignedOut()
    {
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Email = "user@example.com";
        viewModel.Password = "дуже-довгий-пароль";

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal([("user@example.com", "дуже-довгий-пароль")], _client.Registrations);
        Assert.False(viewModel.IsSignedIn);
        Assert.Contains("Увійдіть", viewModel.InfoMessage, StringComparison.Ordinal);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Register_WeakPassword_ShowsServerExplanation()
    {
        _client.Error = new BackupApiException(HttpStatusCode.BadRequest, "Пароль має містити щонайменше 12 символів.");
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Email = "user@example.com";
        viewModel.Password = "коротко";

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Пароль має містити щонайменше 12 символів.", viewModel.ErrorMessage);
        Assert.False(viewModel.HasInfo);
    }

    [Fact]
    public async Task Logout_ClearsSessionAndShowsSignedOut()
    {
        _client.IsSignedIn = true;
        var viewModel = _client.CreateViewModel(_server);
        viewModel.Password = "дуже-довгий-пароль";

        await viewModel.LogoutCommand.ExecuteAsync(null);

        Assert.Equal(1, _client.Logouts);
        Assert.False(viewModel.IsSignedIn);
        Assert.Null(viewModel.SignedInEmail);
        Assert.Equal("Ви не увійшли.", viewModel.StatusText);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.True(viewModel.CanLogin);
    }

    [Fact]
    public async Task Refresh_SavedSession_ShowsEmailFromServer()
    {
        _client.IsSignedIn = true;
        _client.CurrentEmail = "saved@example.com";
        var viewModel = _client.CreateViewModel(_server);
        Assert.Equal("Ви увійшли.", viewModel.StatusText);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSignedIn);
        Assert.Equal("Ви увійшли як saved@example.com.", viewModel.StatusText);
    }

    [Fact]
    public async Task Refresh_SessionExpired_ShowsSignedOutWithExplanation()
    {
        _client.IsSignedIn = true;
        _client.Error = new BackupApiException(HttpStatusCode.Unauthorized, "Сесія завершилась. Увійдіть знову.");
        var viewModel = _client.CreateViewModel(_server);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSignedIn);
        Assert.Contains("Увійдіть знову", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_ServerUnavailable_KeepsSignedInAndExplains()
    {
        _client.IsSignedIn = true;
        _client.Error = new BackupApiException(null, "Сервер бекапу недоступний. Перевірте підключення й адресу сервера.");
        var viewModel = _client.CreateViewModel(_server);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSignedIn);
        Assert.Contains("недоступний", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveServer_NewAddress_SavesAndAsksForRestart()
    {
        var viewModel = _client.CreateViewModel(_server);
        viewModel.ServerUrl = "https://backup.example.com";

        await viewModel.SaveServerCommand.ExecuteAsync(null);

        Assert.Equal(["https://backup.example.com"], _server.Saved);
        Assert.Contains("Перезапустіть", viewModel.InfoMessage, StringComparison.Ordinal);
        Assert.Contains("Перезапустіть", viewModel.ServerStatusText, StringComparison.Ordinal);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task SaveServer_FirstAddress_StillNotConnectedUntilRestart()
    {
        _server.ActiveUrl = null;
        _server.SavedUrl = string.Empty;
        var viewModel = _client.CreateViewModel(_server);
        viewModel.ServerUrl = "https://backup.example.com";

        await viewModel.SaveServerCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConfigured);
        Assert.False(viewModel.CanLogin);
        Assert.Contains("Перезапустіть", viewModel.ServerStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveServer_InvalidAddress_ShowsErrorAndDoesNotSave()
    {
        _server.ValidationError = "Адреса має починатися з https://.";
        var viewModel = _client.CreateViewModel(_server);
        viewModel.ServerUrl = "http://backup.example.com";

        await viewModel.SaveServerCommand.ExecuteAsync(null);

        Assert.Equal("Адреса має починатися з https://.", viewModel.ErrorMessage);
        Assert.Empty(_server.Saved);
    }

    [Fact]
    public async Task SaveServer_WriteFails_ShowsError()
    {
        _server.SaveError = new IOException("диск заповнений");
        var viewModel = _client.CreateViewModel(_server);
        viewModel.ServerUrl = "https://backup.example.com";

        await viewModel.SaveServerCommand.ExecuteAsync(null);

        Assert.Contains("диск заповнений", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }
}
