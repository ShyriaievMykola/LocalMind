using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMind.Core.Storage.Entities;

/// <summary>Вектор фрагмента, обчислений певною моделлю.</summary>
public sealed class EmbeddingEntity
{
    public Guid ChunkId { get; set; }

    public int ModelId { get; set; }

    /// <summary>Вектор <see cref="float"/> у вигляді байтів (BLOB).</summary>
    public required byte[] Vector { get; set; }

    public ChunkEntity? Chunk { get; set; }

    public EmbeddingModelEntity? Model { get; set; }
}

/// <summary>Схема таблиці векторів: один вектор на пару «фрагмент — модель».</summary>
public sealed class EmbeddingEntityConfiguration : IEntityTypeConfiguration<EmbeddingEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmbeddingEntity> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(StorageChecks.EmbeddingVectorName, StorageChecks.EmbeddingVector));
        builder.HasKey(e => new { e.ChunkId, e.ModelId });
        builder.HasIndex(e => e.ModelId);
        builder.HasOne(e => e.Chunk)
            .WithMany(c => c.Embeddings)
            .HasForeignKey(e => e.ChunkId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Model)
            .WithMany()
            .HasForeignKey(e => e.ModelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
