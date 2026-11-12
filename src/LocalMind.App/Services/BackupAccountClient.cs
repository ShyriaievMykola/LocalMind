using LocalMind.Core.Backup;

namespace LocalMind.App.Services;

/// <summary>Обліковий запис через <see cref="BackupApiClient"/>; токени зберігає сам клієнт.</summary>
internal sealed class BackupAccountClient(BackupApiClient api) : IAccountClient
{
    public bool IsSignedIn => api.IsSignedIn;

    public Task RegisterAsync(string email, string password, CancellationToken cancellationToken = default) =>
        api.RegisterAsync(email, password, cancellationToken);

    public Task LoginAsync(string email, string password, CancellationToken cancellationToken = default) =>
        api.LoginAsync(email, password, cancellationToken);

    public Task LogoutAsync(CancellationToken cancellationToken = default) => api.LogoutAsync(cancellationToken);

    public async Task<string> GetEmailAsync(CancellationToken cancellationToken = default) =>
        (await api.GetCurrentUserAsync(cancellationToken)).Email;
}
