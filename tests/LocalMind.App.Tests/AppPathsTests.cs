using LocalMind.App.Services;
using LocalMind.Core.Configuration;

namespace LocalMind.App.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ResolveAppFile_FileInParentFolder_FindsIt()
    {
        var model = Path.Combine(_root, "models", "model.onnx");
        var baseDirectory = Path.Combine(_root, "bin", "Debug");
        Directory.CreateDirectory(Path.GetDirectoryName(model)!);
        Directory.CreateDirectory(baseDirectory);
        File.WriteAllText(model, string.Empty);

        Assert.Equal(model, AppPaths.ResolveAppFile("models/model.onnx", baseDirectory));
    }

    [Fact]
    public void ResolveAppFile_FileMissing_ReturnsPathInsideBaseDirectory()
    {
        var baseDirectory = Path.Combine(_root, "bin");
        Directory.CreateDirectory(baseDirectory);

        Assert.Equal(
            Path.Combine(baseDirectory, "models", "model.onnx"),
            AppPaths.ResolveAppFile(Path.Combine("models", "model.onnx"), baseDirectory));
    }

    [Fact]
    public void ResolveAppFile_AbsolutePath_ReturnsUnchanged()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "model.onnx");

        Assert.Equal(path, AppPaths.ResolveAppFile(path, _root));
    }

    [Fact]
    public void ResolveDatabase_EmptyOrRelative_IsInsideUserDataFolder()
    {
        Directory.CreateDirectory(_root);
        var dataFolder = Path.GetDirectoryName(StorageOptions.DefaultDatabasePath())!;

        Assert.Equal(StorageOptions.DefaultDatabasePath(), AppPaths.ResolveDatabase(string.Empty));
        Assert.Equal(Path.Combine(dataFolder, "other.db"), AppPaths.ResolveDatabase("other.db"));
    }
}
