using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using LocalMind.Core.Backup.Dtos;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

/// <summary>Бекапи без сервера: «сервер» — список в пам'яті, помилку і зупинку задає тест.</summary>
internal sealed class FakeBackupOperations : IBackupOperations
{
    public List<BackupInfo> Server { get; } = [];

    /// <summary>Кидається з кожної операції.</summary>
    public Exception? Error { get; set; }

    /// <summary>Поки не завершено, операції чекають; скасування перериває очікування.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public List<string> CreatedWith { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public List<(Guid Id, string Password)> Restored { get; } = [];

    public int ListCalls { get; private set; }

    public async Task<BackupInfo> CreateAsync(string password, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken);
        CreatedWith.Add(password);
        var info = Backup(2048, DateTimeOffset.UtcNow);
        Server.Add(info);
        return info;
    }

    public async Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        await EnterAsync(cancellationToken);
        return [.. Server];
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken);
        Deleted.Add(id);
        Server.RemoveAll(info => info.Id == id);
    }

    public async Task RestoreAsync(Guid id, string password, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken);
        Restored.Add((id, password));
    }

    public static BackupInfo Backup(long sizeBytes, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), sizeBytes, "00", createdAt);

    public BackupViewModel CreateViewModel(FakeAccountClient? account = null, IBackupServerSettings? server = null) =>
        new(
            this,
            account ?? new FakeAccountClient { IsSignedIn = true },
            server ?? new FakeBackupServerSettings(),
            NullLogger<BackupViewModel>.Instance);

    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        if (Error is not null)
        {
            throw Error;
        }
    }
}
