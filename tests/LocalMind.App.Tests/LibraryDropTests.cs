using LocalMind.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

public sealed class LibraryDropTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("localmind-drop-").FullName;
    private readonly FakeIndexingService _indexing = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Drop_Folder_AddsItForIndexing()
    {
        var notes = CreateFolder("notes");
        var viewModel = _indexing.CreateViewModel();

        await viewModel.AddPathsAsync([notes]);

        Assert.Equal([notes], _indexing.AddedFolders);
        Assert.Equal([notes], viewModel.Folders);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(viewModel.NoticeMessage);
    }

    [Fact]
    public async Task Drop_SupportedFile_AddsItsFolder_AndExplains()
    {
        var notes = CreateFolder("notes");
        var file = CreateFile(notes, "idea.md");
        var viewModel = _indexing.CreateViewModel();

        await viewModel.AddPathsAsync([file]);

        Assert.Equal([notes], _indexing.AddedFolders);
        Assert.Contains("idea.md", viewModel.NoticeMessage, StringComparison.Ordinal);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Drop_SeveralFilesFromOneFolder_AddsFolderOnce()
    {
        var notes = CreateFolder("notes");
        var viewModel = _indexing.CreateViewModel();

        await viewModel.AddPathsAsync([CreateFile(notes, "a.md"), CreateFile(notes, "b.txt")]);

        Assert.Equal([notes], _indexing.AddedFolders);
    }

    [Fact]
    public async Task Drop_UnsupportedFile_ExplainsSupportedFormats_AndAddsNothing()
    {
        var image = CreateFile(_root, "photo.png");
        var viewModel = _indexing.CreateViewModel();

        await viewModel.AddPathsAsync([image]);

        Assert.Empty(_indexing.AddedFolders);
        Assert.Contains("photo.png", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(".md", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drop_FolderAndUnsupportedFile_AddsFolderAndReportsFile()
    {
        var notes = CreateFolder("notes");
        var image = CreateFile(_root, "photo.png");
        var viewModel = _indexing.CreateViewModel();

        await viewModel.AddPathsAsync([notes, image]);

        Assert.Equal([notes], _indexing.AddedFolders);
        Assert.Contains("photo.png", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drop_FileFromTrackedFolder_IsNotAddedAgain()
    {
        var notes = CreateFolder("notes");
        await _indexing.Repository.AddSourceFolderAsync(notes);
        var viewModel = _indexing.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.AddPathsAsync([CreateFile(notes, "a.md")]);

        Assert.Empty(_indexing.AddedFolders);
        Assert.Contains("Уже є в бібліотеці", viewModel.NoticeMessage, StringComparison.Ordinal);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Drop_WithoutModel_ExplainsAndAddsNothing()
    {
        var status = new CoreStatus(NullLogger<CoreStatus>.Instance);
        status.SetModelMissing("missing.onnx");
        status.SetReady();
        var viewModel = _indexing.CreateViewModel(status);

        await viewModel.AddPathsAsync([CreateFolder("notes")]);

        Assert.Empty(_indexing.AddedFolders);
        Assert.Contains("модель", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    private string CreateFolder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private static string CreateFile(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "текст");
        return path;
    }
}