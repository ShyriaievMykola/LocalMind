using System.ComponentModel.DataAnnotations;
using LocalMind.Server.Data;
using LocalMind.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Server.Audit;

/// <summary>Коди подій аудиту.</summary>
public static class AuditActions
{
    public const string Registered = "register";
    public const string LoginSucceeded = "login.succeeded";
    public const string LoginFailed = "login.failed";
    public const string RefreshSucceeded = "refresh.succeeded";
    public const string RefreshFailed = "refresh.failed";
    public const string RefreshReuseDetected = "refresh.reuse_detected";
    public const string Logout = "logout";
    public const string SessionsRevokedAll = "sessions.revoked_all";
    public const string BackupUploaded = "backup.uploaded";
    public const string BackupDeleted = "backup.deleted";
    public const string UserBlocked = "user.blocked";
    public const string UserUnblocked = "user.unblocked";
    public const string AdminSeeded = "admin.seeded";
}

/// <summary>Налаштування аудиту (секція <c>Audit</c>).</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Скільки днів зберігати записи; старіші видаляє фонова служба.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 180;
}

/// <summary>
/// Пише події безпеки в БД і в лог (DR-034). У запис потрапляють лише код дії, ідентифікатори, IP і коротка причина
/// з фіксованого переліку: паролі, токени та введений користувачем текст не пишуться. Збій запису не ламає запит.
/// </summary>
public sealed partial class AuditLog(ServerDbContext db, IHttpContextAccessor http, TimeProvider time, ILogger<AuditLog> logger)
{
    /// <summary>Записує подію.</summary>
    public async Task WriteAsync(string action, string? userId = null, string? actorId = null, string? detail = null)
    {
        var ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        LogEvent(logger, action, userId, actorId, ip, detail);

        var entry = new AuditEntryEntity
        {
            OccurredAt = time.GetUtcNow(),
            Action = action,
            UserId = userId,
            ActorId = actorId,
            IpAddress = ip,
            Detail = detail,
        };
        try
        {
            db.AuditEntries.Add(entry);

            // Запит міг бути скасований (обрив з'єднання), але подію однаково треба зберегти.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            db.Entry(entry).State = EntityState.Detached;
            LogWriteFailed(logger, ex, action);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Аудит: {Action} користувач={UserId} виконавець={ActorId} ip={Ip} причина={Detail}")]
    private static partial void LogEvent(ILogger logger, string action, string? userId, string? actorId, string? ip, string? detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося записати подію аудиту {Action}")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, string action);
}
