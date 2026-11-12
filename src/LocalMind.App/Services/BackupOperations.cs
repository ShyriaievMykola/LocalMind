using LocalMind.Core.Abstractions;
using LocalMind.Core.Backup;
using LocalMind.Core.Backup.Dtos;

namespace LocalMind.App.Services;

/// <summary>Бекапи через <see cref="BackupService"/>, <see cref="RestoreService"/> і <see cref="BackupApiClient"/>.</summary>
internal sealed class BackupOperations(
    BackupService backup,
    RestoreService restore,
    BackupApiClient api,
    IIndexingService indexing) : IBackupOperations
{
    public Task<BackupInfo> CreateAsync(string password, CancellationToken cancellationToken = default) =>
        backup.CreateBackupAsync(password, cancellationToken);

    public Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        api.ListBackupsAsync(cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        api.DeleteBackupAsync(id, cancellationToken);

    public Task RestoreAsync(Guid id, string password, CancellationToken cancellationToken = default)
    {
        // Відкрите підключення індексації не дасть замінити файл бази (див. зауваження до RestoreService).
        indexing.CancelPending();
        return restore.RestoreAsync(id, password, cancellationToken);
    }
}
