namespace LocalMind.Server.Storage;

/// <summary>
/// Пропускає по одному завантаженню бекапу на користувача: перевірка квоти, запис файла й запис у БД мають
/// виконуватись як одне ціле, інакше паралельні запити всі пройшли б перевірку до того, як хтось додав запис (DR-032).
/// Сервер — один процес, тож замка в пам'яті достатньо.
/// </summary>
public sealed class UserUploadGate
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Entry> _entries = [];

    /// <summary>Чекає своєї черги для користувача; звільнення — через <see cref="IDisposable.Dispose"/> результату.</summary>
    public async Task<IDisposable> EnterAsync(string userId, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(userId, out entry!))
            {
                entry = new Entry();
                _entries[userId] = entry;
            }

            entry.Users++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Leave(userId, entry, release: false);
            throw;
        }

        return new Releaser(this, userId, entry);
    }

    private void Leave(string userId, Entry entry, bool release)
    {
        if (release)
        {
            entry.Semaphore.Release();
        }

        lock (_sync)
        {
            // Запис видаляємо, коли ніхто не чекає: словник не росте разом із кількістю користувачів.
            if (--entry.Users == 0)
            {
                _entries.Remove(userId);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Users { get; set; }
    }

    private sealed class Releaser(UserUploadGate gate, string userId, Entry entry) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Leave(userId, entry, release: true);
            }
        }
    }
}
