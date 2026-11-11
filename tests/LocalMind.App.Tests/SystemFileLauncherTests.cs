using System.Diagnostics;
using LocalMind.App.Services;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion.Parsers;
using LocalMind.Core.Models;

namespace LocalMind.App.Tests;

public sealed class SystemFileLauncherTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-launch-").FullName;
    private readonly ParserRegistry _parsers = new([
        new PlainTextParser(), new MarkdownParser(), new PdfParser(), new DocxParser(),
    ]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_ExistingFile_UsesShellAssociationWithFullPath(bool relative)
    {
        var fullPath = Path.Combine(_folder, "конспект & план з пробілами.txt");
        await File.WriteAllTextAsync(fullPath, "нотатка");
        ProcessStartInfo? started = null;
        var launcher = new SystemFileLauncher(_parsers, info => started = info);
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
        var launcher = new SystemFileLauncher(_parsers, _ => launched = true);
        var missing = Path.Combine(_folder, "missing.md");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => launcher.OpenAsync(missing));

        Assert.Equal(missing, exception.FileName);
        Assert.False(launched);
    }

    [Fact]
    public async Task Open_Cancelled_DoesNotLaunchAnything()
    {
        var launched = false;
        var launcher = new SystemFileLauncher(_parsers, _ => launched = true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launcher.OpenAsync(Path.Combine(_folder, "note.md"), cancellation.Token));

        Assert.False(launched);
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".TXT")]
    [InlineData(".md")]
    [InlineData(".markdown")]
    [InlineData(".pdf")]
    [InlineData(".PDF")]
    [InlineData(".docx")]
    [InlineData(".DOCX")]
    public async Task Open_RegisteredDocumentFormat_IsAllowed(string extension)
    {
        var path = Path.Combine(_folder, "note" + extension);
        await File.WriteAllTextAsync(path, "нотатка");
        var launched = false;
        var launcher = new SystemFileLauncher(_parsers, _ => launched = true);

        await launcher.OpenAsync(path);

        Assert.True(launched);
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    [InlineData(".ps1")]
    [InlineData(".sh")]
    [InlineData(".lnk")]
    [InlineData("")]
    [InlineData(".md.exe")]
    [InlineData(".txt.bat")]
    public async Task Open_UnsupportedFormat_NeverLaunchesProcess(string extension)
    {
        var path = Path.Combine(_folder, "note" + extension);
        await File.WriteAllTextAsync(path, "нешкідливий тестовий вміст");
        var launched = false;
        var launcher = new SystemFileLauncher(_parsers, _ => launched = true);

        var exception = await Assert.ThrowsAsync<LocalMindException>(() => launcher.OpenAsync(path));

        Assert.Equal("Цей формат файла не підтримується.", exception.Message);
        Assert.False(launched);
    }

    [Fact]
    public async Task Open_FormatsComeFromInjectedRegistry()
    {
        var custom = Path.Combine(_folder, "custom.note");
        var text = Path.Combine(_folder, "text.txt");
        await File.WriteAllTextAsync(custom, "нотатка");
        await File.WriteAllTextAsync(text, "нотатка");
        var paths = new List<string>();
        var launcher = new SystemFileLauncher(new ParserRegistry([new NoteParser()]), info => paths.Add(info.FileName));

        await launcher.OpenAsync(custom);
        await Assert.ThrowsAsync<LocalMindException>(() => launcher.OpenAsync(text));

        Assert.Equal(custom, Assert.Single(paths));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class NoteParser : IDocumentParser
    {
        public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string> { ".note" };

        public Task<ParsedDocument> ParseAsync(Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ParsedDocument("нотатка"));
    }
}
