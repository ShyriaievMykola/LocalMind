using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Storage;
using LocalMind.TestUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Core.Tests.Storage;

public class StorageRegistrationTests
{
    [Fact]
    public void DefaultDatabasePath_IsInsideApplicationFolder()
    {
        var path = StorageOptions.DefaultDatabasePath();

        Assert.True(Path.IsPathRooted(path));
        Assert.Equal("localmind.db", Path.GetFileName(path));
        Assert.Equal("LocalMind", Path.GetFileName(Path.GetDirectoryName(path)));
    }

    [Fact]
    public async Task Initialize_CreatesDatabaseFileAndFolder_AndDataSurvivesRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var databasePath = Path.Combine(folder, "nested", "test.db");

        try
        {
            var document = TestData.Document();

            await using (var first = BuildProvider(databasePath))
            {
                await first.InitializeLocalMindStorageAsync();
                await first.GetRequiredService<IDocumentRepository>().SaveAsync(document, [], [], "model");
            }

            Assert.True(File.Exists(databasePath));

            await using var second = BuildProvider(databasePath);
            await second.InitializeLocalMindStorageAsync();
            Assert.Equal(document, await second.GetRequiredService<IDocumentRepository>().GetDocumentAsync(document.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static ServiceProvider BuildProvider(string databasePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{StorageOptions.SectionName}:{nameof(StorageOptions.DatabasePath)}"] = databasePath,
            })
            .Build();

        return new ServiceCollection().AddLocalMindCore(configuration).BuildServiceProvider();
    }
}
