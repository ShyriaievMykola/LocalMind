using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMind.Core.Storage.Entities;

/// <summary>Embedding-модель, вектори якої є у сховищі.</summary>
public sealed class EmbeddingModelEntity
{
    public int Id { get; set; }

    public required string Name { get; set; }
}

/// <summary>Схема таблиці моделей: назва унікальна.</summary>
public sealed class EmbeddingModelEntityConfiguration : IEntityTypeConfiguration<EmbeddingModelEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmbeddingModelEntity> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(StorageChecks.EmbeddingModelNameName, StorageChecks.EmbeddingModelName));
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => m.Name).IsUnique();
    }
}
