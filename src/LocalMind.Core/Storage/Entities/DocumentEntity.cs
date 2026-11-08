using LocalMind.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMind.Core.Storage.Entities;

/// <summary>Проіндексований файл.</summary>
public sealed class DocumentEntity
{
    public Guid Id { get; set; }

    public required string Path { get; set; }

    public required string ContentHash { get; set; }

    public DocumentStatus Status { get; set; }

    public DateTimeOffset IndexedAt { get; set; }

    public string? Error { get; set; }

    public List<ChunkEntity> Chunks { get; set; } = [];
}

/// <summary>Схема таблиці документів: шлях до файла унікальний.</summary>
public sealed class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => d.Path).IsUnique();
        builder.Property(d => d.ContentHash).HasMaxLength(Document.ContentHashLength);
        builder.Property(d => d.Status).HasConversion<string>();
    }
}
