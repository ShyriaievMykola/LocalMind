using System.Diagnostics;
using LocalMind.App.Services;

namespace LocalMind.App.Tests;

public sealed class SystemFileLauncherTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-launch-").FullName;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_ExistingFile_UsesShellAssociationWithFullPath(bool relative)
    {
        var fullPath = Path.Combine(_folder, "конспект & план з пробілами.txt");
        await File.WriteAllTextAsync(fullPath, "нотатка");
        ProcessStartInfo? started = null;
        var launcher = new SystemFileLauncher(info => started = info);
        var supplied = relative ? Path.GetRelativePath(Environment.CurrentDirectory, fullPath) : fullPath;

        await launcher.OpenAsync(supplied);

        Assert.NotNull(started);
        Assert.Equal(fullPath, started.FileName);
        Assert.Equal(_folder, started.WorkingDirectory);
        Assert.True(started.UseShellExecute);
        Assert.Empty(started.ArgumentList);
        Assert.Empty(started.Arguments);
    }

    [Fact]
    public async Task Open_MissingFile_DoesNotLaunchAnything()
    {
        var launched = false;
        var launcher = new SystemFileLauncher(_ => launched = true);
        var missing = Path.Combine(_folder, "missing.md");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => launcher.OpenAsync(missing));

        Assert.Equal(missing, exception.FileName);
        Assert.False(launched);
    }

    [Fact]
    public async Task Open_Cancelled_DoesNotLaunchAnything()
    {
        var launched = false;
        var launcher = new SystemFileLauncher(_ => launched = true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launcher.OpenAsync(Path.Combine(_folder, "note.md"), cancellation.Token));

        Assert.False(launched);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
