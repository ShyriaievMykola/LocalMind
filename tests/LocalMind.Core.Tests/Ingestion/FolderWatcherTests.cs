using LocalMind.Core.Ingestion.Parsers;
using LocalMind.Core.Ingestion.Watching;

namespace LocalMind.Core.Tests.Ingestion;

public sealed class FolderWatcherTests : IDisposable
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _quietPeriod = TimeSpan.FromSeconds(2);

    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-watch-").FullName;
    private readonly FolderWatcher _watcher = new(
        new ParserRegistry([new PlainTextParser(), new MarkdownParser(), new DocxParser()]));

    public void Dispose()
    {
        _watcher.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task CreateFile_ReportsFileChanged()
    {
        _watcher.Watch(_folder);

        await File.WriteAllTextAsync(Path.Combine(_folder, "note.md"), "текст");

        var changes = await ReadUntilAsync("note.md");
        Assert.Contains(changes, c => IsFile(c, "note.md"));
    }

    [Fact]
    public async Task ModifyFile_ReportsFileChanged()
    {
        var path = Path.Combine(_folder, "note.md");
        await File.WriteAllTextAsync(path, "стара версія");
        _watcher.Watch(_folder);

        await File.AppendAllTextAsync(path, " і нова");

        var changes = await ReadUntilAsync("note.md");
        Assert.Contains(changes, c => IsFile(c, "note.md"));
    }

    [Fact]
    public async Task RenameFile_ReportsOldAndNewPaths()
    {
        var oldPath = Path.Combine(_folder, "old.md");
        await File.WriteAllTextAsync(oldPath, "текст");
        _watcher.Watch(_folder);

        File.Move(oldPath, Path.Combine(_folder, "new.md"));

        var changes = await ReadUntilAsync("old.md", "new.md");
        Assert.Contains(changes, c => IsFile(c, "old.md"));
        Assert.Contains(changes, c => IsFile(c, "new.md"));
    }

    [Fact]
    public async Task DeleteFile_ReportsFileChanged()
    {
        var path = Path.Combine(_folder, "note.md");
        await File.WriteAllTextAsync(path, "текст");
        _watcher.Watch(_folder);

        File.Delete(path);

        var changes = await ReadUntilAsync("note.md");
        Assert.Contains(changes, c => IsFile(c, "note.md"));
    }

    [Fact]
    public async Task FileInSubfolder_IsReported()
    {
        var subfolder = Directory.CreateDirectory(Path.Combine(_folder, "sub")).FullName;
        _watcher.Watch(_folder);

        await File.WriteAllTextAsync(Path.Combine(subfolder, "inner.txt"), "текст");

        var changes = await ReadUntilAsync("inner.txt");
        Assert.Contains(changes, c => IsFile(c, "inner.txt"));
    }

    [Fact]
    public async Task SeriesOfSaves_ReportsSingleChange()
    {
        var path = Path.Combine(_folder, "note.md");
        _watcher.Watch(_folder);

        for (var i = 1; i <= 5; i++)
        {
            await File.WriteAllTextAsync(path, new string('x', i));
        }

        await ReadUntilAsync("note.md");
        var later = await ReadForAsync(_quietPeriod);
        Assert.DoesNotContain(later, c => IsFile(c, "note.md"));
    }

    [Theory]
    [InlineData("~$report.docx")]
    [InlineData("draft.tmp")]
    [InlineData(".note.md.swp")]
    [InlineData("image.png")]
    public async Task IgnoredFile_IsNotReported(string fileName)
    {
        _watcher.Watch(_folder);

        await File.WriteAllTextAsync(Path.Combine(_folder, fileName), "x");
        await File.WriteAllTextAsync(Path.Combine(_folder, "marker.txt"), "x");

        // Подія ігнорованого файла, якби вона була, прийшла б раніше за маркер.
        var changes = await ReadUntilAsync("marker.txt");
        Assert.DoesNotContain(changes, c => Path.GetFileName(c.Path) == fileName);
    }

    [Fact]
    public async Task FolderMovedIn_ReportsRescan()
    {
        var outside = Directory.CreateTempSubdirectory("localmind-outside-").FullName;
        await File.WriteAllTextAsync(Path.Combine(outside, "inner.md"), "текст");
        _watcher.Watch(_folder);

        Directory.Move(outside, Path.Combine(_folder, "moved"));

        var changes = await ReadUntilAsync("moved");
        Assert.Contains(changes, c =>
            c.Kind == FolderChangeKind.RescanRequired && Path.GetFileName(c.Path) == "moved");
    }

    [Fact]
    public async Task WatcherError_ReportsRescanOfWholeFolder()
    {
        _watcher.Watch(_folder);

        _watcher.OnWatcherError(_folder);

        var changes = await ReadUntilAsync(Path.GetFileName(_folder));
        Assert.Contains(changes, c => c.Kind == FolderChangeKind.RescanRequired && c.Path == _folder);
    }

    [Fact]
    public async Task Unwatch_StopsReporting()
    {
        _watcher.Watch(_folder);
        _watcher.Unwatch(_folder);

        await File.WriteAllTextAsync(Path.Combine(_folder, "note.md"), "текст");

        Assert.Empty(await ReadForAsync(_quietPeriod));
    }

    [Fact]
    public async Task Unwatch_DiscardsPendingChanges()
    {
        _watcher.Watch(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "note.md"), "текст");
        await Task.Delay(100);

        _watcher.Unwatch(_folder);

        Assert.Empty(await ReadForAsync(_quietPeriod));
    }

    [Fact]
    public void Watch_SameFolderTwice_DoesNotThrow()
    {
        _watcher.Watch(_folder);

        var exception = Record.Exception(() => _watcher.Watch(_folder + Path.DirectorySeparatorChar));

        Assert.Null(exception);
    }

    [Fact]
    public void Watch_MissingFolder_ThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(() => _watcher.Watch(Path.Combine(_folder, "missing")));
    }

    [Fact]
    public async Task Dispose_CompletesQueueAndRejectsWatch()
    {
        _watcher.Dispose();

        await _watcher.Changes.Completion.WaitAsync(_waitTimeout);
        Assert.Throws<ObjectDisposedException>(() => _watcher.Watch(_folder));
    }

    [Fact]
    public void Constructor_NegativeDebounce_ThrowsArgumentOutOfRangeException()
    {
        var parsers = new ParserRegistry([new PlainTextParser()]);

        Assert.Throws<ArgumentOutOfRangeException>(() => new FolderWatcher(parsers, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Constructor_NullParsers_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FolderWatcher(null!));
    }

    /// <summary>Порівнюємо за іменем: на macOS тимчасова папка може мати інший префікс шляху.</summary>
    private static bool IsFile(FolderChange change, string fileName) =>
        change.Kind == FolderChangeKind.FileChanged && Path.GetFileName(change.Path) == fileName;

    /// <summary>Читає чергу, доки не надійдуть події для всіх імен; повертає все прочитане.</summary>
    private async Task<List<FolderChange>> ReadUntilAsync(params string[] names)
    {
        var received = new List<FolderChange>();
        var missing = names.ToHashSet();
        using var cts = new CancellationTokenSource(_waitTimeout);

        try
        {
            while (missing.Count > 0)
            {
                var change = await _watcher.Changes.ReadAsync(cts.Token);
                received.Add(change);
                missing.Remove(Path.GetFileName(change.Path));
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Не надійшли події для: " + string.Join(", ", missing));
        }

        return received;
    }

    /// <summary>Збирає все, що надійде в чергу протягом вказаного часу.</summary>
    private async Task<List<FolderChange>> ReadForAsync(TimeSpan duration)
    {
        var received = new List<FolderChange>();
        using var cts = new CancellationTokenSource(duration);

        try
        {
            while (true)
            {
                received.Add(await _watcher.Changes.ReadAsync(cts.Token));
            }
        }
        catch (OperationCanceledException)
        {
            // Час вийшов — повертаємо зібране.
        }

        return received;
    }
}