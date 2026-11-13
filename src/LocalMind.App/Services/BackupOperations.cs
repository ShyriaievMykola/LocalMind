using LocalMind.Core.Abstractions;
using LocalMind.Core.Backup;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Core.Errors;

namespace LocalMind.App.Services;

/// <summary>Бекапи через <see cref="BackupService"/>, <see cref="RestoreService"/> і <see cref="BackupApiClient"/>.</summary>
internal sealed class BackupOperations(
    BackupService backup,
    RestoreService restore,
    BackupApiClient api,
    IIndexingService indexing) : IBackupOperations
{
    private const string ReplaceFailedMessage =
        "Не вдалося замінити базу. Закрийте інші вікна LocalMind та програми, що відкривають її файл, і спробуйте ще раз.";

    private const string IndexingStoppedNote = "Індексацію зупинено — запустіть пересканування в «Бібліотеці».";

    public Task<BackupInfo> CreateAsync(string password, CancellationToken cancellationToken = default) =>
        backup.CreateBackupAsync(password, cancellationToken);

    public Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        api.ListBackupsAsync(cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        api.DeleteBackupAsync(id, cancellationToken);

    public async Task RestoreAsync(Guid id, string password, CancellationToken cancellationToken = default)
    {
        var indexingStopped = false;
        try
        {
            // Індексацію зупиняємо лише перед самою заміною файла (бекап уже завантажено й розшифровано):
            // неправильний пароль чи обрив мережі не повинні губити чергу індексації. Відкрите підключення
            // індексації не дасть замінити файл бази (див. зауваження до RestoreService).
            await restore.RestoreAsync(id, password, () =>
            {
                indexing.CancelPending();
                indexingStopped = true;
            }, cancellationToken);
        }
        catch (Exception exception) when (indexingStopped)
        {
            // Поточна база ціла (RestoreService повертає її), але сире повідомлення системи користувачу нічого не каже.
            // Заміна файла (File.Replace) падає, коли базу тримає відкритою інше вікно LocalMind чи інша програма.
            var message = exception switch
            {
                IOException or UnauthorizedAccessException => ReplaceFailedMessage,
                OperationCanceledException => "Відновлення скасовано.",
                _ => exception.Message,
            };
            throw new LocalMindException($"{message} {IndexingStoppedNote}", exception);
        }

        // Спостерігач міг додати файли в чергу вже після заміни: вони потрапили б в нову базу при старому індексі.
        indexing.CancelPending();
    }
}
