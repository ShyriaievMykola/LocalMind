using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMind.Server.Data.Entities;

/// <summary>
/// Refresh-токен. Зберігається лише SHA-256 від токена. Усі токени однієї сесії (від входу до виходу)
/// мають спільний <see cref="SessionId"/> — його й відкликаємо при logout або повторному використанні.
/// </summary>
public sealed class RefreshTokenEntity
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public IdentityUser? User { get; set; }

    public Guid SessionId { get; set; }

    /// <summary>SHA-256 токена (hex).</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Коли токен обміняли на новий; повторне пред'явлення після цього — ознака крадіжки.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Схема таблиці refresh-токенів: хеш унікальний, токени видаляються разом з користувачем.</summary>
public sealed class RefreshTokenEntityConfiguration : IEntityTypeConfiguration<RefreshTokenEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshTokenEntity> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.SessionId);
        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
