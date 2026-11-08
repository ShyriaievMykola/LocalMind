using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMind.Core.Storage.Entities;

/// <summary>Фрагмент документа.</summary>
public sealed class ChunkEntity
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public int Ordinal { get; set; }

    public required string Text { get; set; }

    public int StartOffset { get; set; }

    public DocumentEntity? Document { get; set; }

    public List<EmbeddingEntity> Embeddings { get; set; } = [];
}

/// <summary>Схема таблиці фрагментів: видалення документа видаляє його фрагменти.</summary>
public sealed class ChunkEntityConfiguration : IEntityTypeConfiguration<ChunkEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ChunkEntity> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.DocumentId, c.Ordinal }).IsUnique();
        builder.HasOne(c => c.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
