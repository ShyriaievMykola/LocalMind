using LocalMind.Core.Models;
using LocalMind.Core.Storage;
using LocalMind.Core.Storage.FullText;
using LocalMind.TestUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Core.Tests.Storage;

/// <summary>CHECK-обмеження схеми (C5): база сама відхиляє некоректні значення; міграція не губить дані й не ламає FTS5.</summary>
public sealed class StorageConstraintsTests : IAsyncLifetime, IDisposable
{
    private const string BeforeChecks = "20261111120000_AddChunksFts";
    private const string Model = "test-model";

    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private TestContextFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _factory = new TestContextFactory(new DbContextOptionsBuilder<LocalMindDbContext>().UseSqlite(_connection).Options);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ValidRows_AreAccepted()
    {
        await MigrateAsync();

        var document = await InsertDocumentAsync("Indexed");
        await ExecuteAsync("INSERT INTO Chunks (Id, DocumentId, Ordinal, Text, StartOffset) VALUES ($id, $doc, 0, 'текст', 0)",
            ("$id", Guid.NewGuid()), ("$doc", document));
        await ExecuteAsync("INSERT INTO SourceFolders (Path) VALUES ('/notes')");

        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM Chunks"));
    }

    [Theory]
    [InlineData("Ordinal", -1, 0)]
    [InlineData("StartOffset", 0, -1)]
    public async Task Chunk_NegativeOrdinalOrOffset_IsRejectedByTheDatabase(string column, int ordinal, int offset)
    {
        await MigrateAsync();
        var document = await InsertDocumentAsync("Indexed");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            "INSERT INTO Chunks (Id, DocumentId, Ordinal, Text, StartOffset) VALUES ($id, $doc, $ordinal, 'текст', $offset)",
            ("$id", Guid.NewGuid()), ("$doc", document), ("$ordinal", ordinal), ("$offset", offset)));

        Assert.Contains("CHECK", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"CK_Chunks_{column}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("indexed")]
    [InlineData("Unknown")]
    [InlineData("0")]
    public async Task Document_UnknownStatus_IsRejectedByTheDatabase(string status)
    {
        await MigrateAsync();

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertDocumentAsync(status));

        Assert.Contains("CK_Documents_Status", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Document_EveryEnumValueIsAccepted()
    {
        await MigrateAsync();

        foreach (var status in Enum.GetNames<DocumentStatus>())
        {
            await InsertDocumentAsync(status);
        }

        Assert.Equal((long)Enum.GetNames<DocumentStatus>().Length, await ScalarAsync("SELECT COUNT(*) FROM Documents"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task Embedding_EmptyOrNotWholeFloatsVector_IsRejectedByTheDatabase(int bytes)
    {
        await MigrateAsync();
        var chunk = await InsertChunkAsync();
        var model = await InsertModelAsync("m1");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            "INSERT INTO Embeddings (ChunkId, ModelId, Vector) VALUES ($chunk, $model, $vector)",
            ("$chunk", chunk), ("$model", model), ("$vector", new byte[bytes])));

        Assert.Contains("CK_Embeddings_Vector", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Embedding_WholeFloatsVector_IsAccepted()
    {
        await MigrateAsync();
        var chunk = await InsertChunkAsync();
        var model = await InsertModelAsync("m1");

        await ExecuteAsync("INSERT INTO Embeddings (ChunkId, ModelId, Vector) VALUES ($chunk, $model, $vector)",
            ("$chunk", chunk), ("$model", model), ("$vector", new byte[8]));

        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM Embeddings"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ModelNameAndFolderPath_Blank_AreRejectedByTheDatabase(string value)
    {
        await MigrateAsync();

        var model = await Assert.ThrowsAsync<SqliteException>(() => InsertModelAsync(value));
        var folder = await Assert.ThrowsAsync<SqliteException>(
            () => ExecuteAsync("INSERT INTO SourceFolders (Path) VALUES ($path)", ("$path", value)));

        Assert.Contains("CK_EmbeddingModels_Name", model.Message, StringComparison.Ordinal);
        Assert.Contains("CK_SourceFolders_Path", folder.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_StillSavesAndReadsEverythingItWrites()
    {
        await MigrateAsync();
        var repository = new DocumentRepository(_factory);
        var document = TestData.Document("/notes/a.md");
        var chunks = TestData.Chunks(document.Id, "перший", "другий");

        await repository.SaveAsync(document, chunks, [new float[] { 1, 0 }, new float[] { 0, 1 }], Model);
        await repository.SaveAsync(TestData.Document("/notes/empty.md", DocumentStatus.Empty), [], [], Model);
        await repository.AddSourceFolderAsync("/notes");

        Assert.Equal(2, (await repository.GetDocumentsAsync()).Count);
        Assert.Equal(2, await repository.GetVectorsAsync(Model).CountAsync());
    }

    [Fact]
    public async Task Migration_KeepsExistingDataAndFullTextSearch()
    {
        await MigrateAsync(BeforeChecks);
        var repository = new DocumentRepository(_factory);
        var kept = TestData.Document("/notes/kept.md");
        var removed = TestData.Document("/notes/removed.md");
        var keptChunks = TestData.Chunks(kept.Id, "давня кава", "давній чай", "стара вода");
        await repository.SaveAsync(kept, keptChunks, [new float[] { 1, 0 }, new float[] { 0, 1 }, new float[] { 1, 1 }], Model);
        await repository.SaveAsync(removed, TestData.Chunks(removed.Id, "зайвий текст"), [new float[] { 1, 0 }], Model);
        await repository.SaveAsync(TestData.Document("/notes/error.md", DocumentStatus.Error), [], [], Model);
        await repository.AddSourceFolderAsync("/notes");
        // Прогалина в rowid: після видалення нові rowid при копіюванні таблиці не збігатимуться зі старими.
        await repository.DeleteAsync(removed.Id);

        await MigrateAsync();

        Assert.Equal(2L, await ScalarAsync("SELECT COUNT(*) FROM Documents"));
        Assert.Equal(3L, await ScalarAsync("SELECT COUNT(*) FROM Chunks"));
        Assert.Equal(3L, await ScalarAsync("SELECT COUNT(*) FROM Embeddings"));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM EmbeddingModels"));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM SourceFolders"));
        Assert.Equal(3, (await repository.GetChunksByDocumentAsync(kept.Id, 0, 10)).Count);
        Assert.Equal(DocumentStatus.Error, (await repository.GetDocumentByPathAsync("/notes/error.md"))!.Status);
        Assert.Equal(3, await repository.GetVectorsAsync(Model).CountAsync());
        var index = new Fts5KeywordIndex(_factory);
        Assert.Equal(keptChunks[0].Id, Assert.Single(await index.SearchAsync("кава", 10)).ChunkId);
        Assert.Equal(keptChunks[2].Id, Assert.Single(await index.SearchAsync("вода", 10)).ChunkId);
        Assert.Empty(await index.SearchAsync("зайвий", 10));
        await AssertIntegrityAsync();
    }

    [Fact]
    public async Task Migration_RecreatesTheFullTextTriggers()
    {
        await MigrateAsync(BeforeChecks);

        await MigrateAsync();

        var triggers = await ColumnAsync("SELECT name FROM sqlite_master WHERE type = 'trigger' ORDER BY name");
        Assert.Equal(["Chunks_AfterDelete", "Chunks_AfterInsert", "Chunks_AfterUpdate"], triggers);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ChunksFts'"));
    }

    [Fact]
    public async Task AfterMigration_InsertUpdateDeleteAndCascadeKeepTheIndexInSync()
    {
        await MigrateAsync(BeforeChecks);
        await MigrateAsync();
        var repository = new DocumentRepository(_factory);
        var index = new Fts5KeywordIndex(_factory);
        var document = TestData.Document("/notes/new.md");
        var chunks = TestData.Chunks(document.Id, "нова кава", "новий чай");
        await repository.SaveAsync(document, chunks, [new float[] { 1, 0 }, new float[] { 0, 1 }], Model);
        Assert.Single(await index.SearchAsync("кава", 10));

        await ExecuteAsync("UPDATE Chunks SET Text = 'нове молоко' WHERE Id = $id", ("$id", chunks[0].Id));
        Assert.Empty(await index.SearchAsync("кава", 10));
        Assert.Single(await index.SearchAsync("молоко", 10));

        await repository.DeleteAsync(document.Id);
        Assert.Empty(await index.SearchAsync("молоко", 10));
        Assert.Empty(await index.SearchAsync("чай", 10));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM Embeddings"));
        await AssertIntegrityAsync();
    }

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = _factory.CreateDbContext();
        await context.Database.MigrateAsync(target);
    }

    private async Task AssertIntegrityAsync()
    {
        Assert.Equal("ok", await ScalarAsync("PRAGMA integrity_check"));
        Assert.Empty(await ColumnAsync("PRAGMA foreign_key_check"));
        // Для FTS5 з external content це звіряє індекс з таблицею Chunks; при розбіжності кидає виняток.
        await ExecuteAsync("INSERT INTO ChunksFts(ChunksFts, rank) VALUES ('integrity-check', 1)");
    }

    private async Task<Guid> InsertDocumentAsync(string status)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO Documents (Id, Path, ContentHash, Status, IndexedAt) VALUES ($id, $path, $hash, $status, '2026-11-13 00:00:00+00:00')",
            ("$id", id), ("$path", $"/notes/{id}.md"), ("$hash", new string('a', 64)), ("$status", status));
        return id;
    }

    private async Task<Guid> InsertChunkAsync()
    {
        var document = await InsertDocumentAsync("Indexed");
        var id = Guid.NewGuid();
        await ExecuteAsync("INSERT INTO Chunks (Id, DocumentId, Ordinal, Text, StartOffset) VALUES ($id, $doc, 0, 'текст', 0)",
            ("$id", id), ("$doc", document));
        return id;
    }

    private async Task<long> InsertModelAsync(string name)
    {
        await ExecuteAsync("INSERT INTO EmbeddingModels (Name) VALUES ($name)", ("$name", name));
        return (long)(await ScalarAsync("SELECT Id FROM EmbeddingModels WHERE Name = $name", ("$name", name)))!;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteScalarAsync();
    }

    private async Task<List<string>> ColumnAsync(string sql)
    {
        await using var command = Command(sql, []);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetValue(0).ToString() ?? string.Empty);
        }

        return values;
    }

    private SqliteCommand Command(string sql, (string Name, object Value)[] parameters)
    {
        var command = _connection.CreateCommand();
#pragma warning disable CA2100 // У тестах SQL — лише літерали з цього файла, користувацького вводу немає.
        command.CommandText = sql;
#pragma warning restore CA2100
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value is Guid guid ? guid.ToString().ToUpperInvariant() : value);
        }

        return command;
    }

    private sealed class TestContextFactory(DbContextOptions<LocalMindDbContext> options)
        : IDbContextFactory<LocalMindDbContext>
    {
        public LocalMindDbContext CreateDbContext() => new(options);
    }
}
