using LocalMind.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocalMind.Core.Storage;

/// <summary>Локальна база SQLite: папки, документи, фрагменти та вектори.</summary>
public sealed class LocalMindDbContext(DbContextOptions<LocalMindDbContext> options) : DbContext(options)
{
    public DbSet<SourceFolderEntity> SourceFolders => Set<SourceFolderEntity>();

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();

    public DbSet<ChunkEntity> Chunks => Set<ChunkEntity>();

    public DbSet<EmbeddingModelEntity> EmbeddingModels => Set<EmbeddingModelEntity>();

    public DbSet<EmbeddingEntity> Embeddings => Set<EmbeddingEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SourceFolderEntity>(folder =>
        {
            folder.HasKey(f => f.Path);
            folder.ToTable(table => table.HasCheckConstraint(StorageChecks.SourceFolderPathName, StorageChecks.SourceFolderPath));
        });
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LocalMindDbContext).Assembly);
    }
}

/// <summary>Створює контекст для <c>dotnet ef migrations</c>; у застосунку не використовується.</summary>
public sealed class LocalMindDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LocalMindDbContext>
{
    /// <inheritdoc />
    public LocalMindDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LocalMindDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
