using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMind.Server.Data.Entities;

/// <summary>Метадані збереженого бекапу; сам шифротекст — у <see cref="Storage.FileSystemBlobStore"/>.</summary>
public sealed class SnapshotEntity
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public IdentityUser? User { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 шифротексту (hex).</summary>
    public required string Sha256 { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Схема таблиці бекапів: пошук за власником, видалення разом з користувачем.</summary>
public sealed class SnapshotEntityConfiguration : IEntityTypeConfiguration<SnapshotEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SnapshotEntity> builder)
    {
        builder.ToTable("Snapshots");
        builder.Property(s => s.Sha256).HasMaxLength(64);
        builder.HasIndex(s => s.UserId);
        builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
