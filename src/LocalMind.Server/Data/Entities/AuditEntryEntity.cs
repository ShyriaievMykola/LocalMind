using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMind.Server.Data.Entities;

/// <summary>Запис аудиту (SV-11). Без паролів, токенів і введеного користувачем тексту.</summary>
public sealed class AuditEntryEntity
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Код дії, напр. <c>login.failed</c>.</summary>
    public required string Action { get; set; }

    /// <summary>Користувач, якого стосується подія; порожній, якщо він невідомий (наприклад, вхід з неіснуючим email).</summary>
    public string? UserId { get; set; }

    /// <summary>Хто виконав дію, якщо це не сам користувач (адміністратор).</summary>
    public string? ActorId { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Коротка причина з фіксованого переліку, напр. <c>wrong_password</c>.</summary>
    public string? Detail { get; set; }
}

/// <summary>Схема таблиці аудиту. Зовнішнього ключа на користувача немає: записи лишаються після видалення акаунта.</summary>
public sealed class AuditEntryEntityConfiguration : IEntityTypeConfiguration<AuditEntryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditEntryEntity> builder)
    {
        builder.ToTable("AuditEntries");
        builder.Property(a => a.Action).HasMaxLength(64);
        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.ActorId).HasMaxLength(450);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.Detail).HasMaxLength(200);
        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => a.UserId);
    }
}
