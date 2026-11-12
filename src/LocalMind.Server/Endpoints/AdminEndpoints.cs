using System.Security.Claims;
using LocalMind.Server.Audit;
using LocalMind.Server.Auth;
using LocalMind.Server.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Server.Endpoints;

/// <summary>Користувач у списку адміністратора. Без хешів і токенів.</summary>
public sealed record AdminUserInfo(string Id, string Email, bool IsAdmin, bool IsBlocked, int BackupCount, long BackupBytes);

/// <summary>Запис аудиту для адміністратора.</summary>
public sealed record AuditRecord(
    long Id, DateTimeOffset OccurredAt, string Action, string? UserId, string? ActorId, string? IpAddress, string? Detail);

/// <summary>Адмін-ендпоінти (SV-08): список користувачів, блокування, перегляд аудиту. Лише роль Admin (DR-034).</summary>
public static class AdminEndpoints
{
    private const int MaxPage = 500;

    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization(AdminAuthorization.PolicyName);
        admin.MapGet("/users", ListUsersAsync);
        admin.MapPost("/users/{id}/block", BlockAsync);
        admin.MapPost("/users/{id}/unblock", UnblockAsync);
        admin.MapGet("/audit", ListAuditAsync);
        return api;
    }

    private static async Task<Ok<AdminUserInfo[]>> ListUsersAsync(
        ServerDbContext db, TimeProvider time, CancellationToken cancellationToken, int skip = 0, int take = 100)
    {
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, MaxPage);
        var now = time.GetUtcNow();

        var users = await db.Users.AsNoTracking().OrderBy(u => u.Email).Skip(skip).Take(take)
            .Select(u => new { u.Id, u.Email, u.LockoutEnd })
            .ToListAsync(cancellationToken);
        var ids = users.Select(u => u.Id).ToList();

        var adminIds = await (from ur in db.UserRoles
                              join role in db.Roles on ur.RoleId equals role.Id
                              where role.Name == AppRoles.Admin && ids.Contains(ur.UserId)
                              select ur.UserId).ToListAsync(cancellationToken);
        var usage = await db.Snapshots.AsNoTracking().Where(s => ids.Contains(s.UserId))
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count(), Bytes = g.Sum(s => s.SizeBytes) })
            .ToDictionaryAsync(g => g.UserId, cancellationToken);

        var result = users.Select(u => new AdminUserInfo(
            u.Id,
            u.Email ?? string.Empty,
            adminIds.Contains(u.Id),
            u.LockoutEnd > now,
            usage.TryGetValue(u.Id, out var used) ? used.Count : 0,
            usage.TryGetValue(u.Id, out used) ? used.Bytes : 0)).ToArray();
        return TypedResults.Ok(result);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> BlockAsync(
        string id, ClaimsPrincipal principal, UserManager<IdentityUser> users, RefreshTokenService refresh, AuditLog audit,
        CancellationToken cancellationToken)
    {
        var actorId = principal.Identity?.Name;
        if (id == actorId)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Не можна заблокувати власний акаунт.");
        }

        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        // Новий SecurityStamp і відкликання сесій: refresh-токени блокованого користувача перестають працювати одразу.
        await users.UpdateSecurityStampAsync(user);
        await refresh.RevokeAllAsync(user.Id, cancellationToken);
        await audit.WriteAsync(AuditActions.UserBlocked, user.Id, actorId);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> UnblockAsync(
        string id, ClaimsPrincipal principal, UserManager<IdentityUser> users, AuditLog audit)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await audit.WriteAsync(AuditActions.UserUnblocked, user.Id, principal.Identity?.Name);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<AuditRecord[]>> ListAuditAsync(
        ServerDbContext db, CancellationToken cancellationToken, string? userId = null, int take = 100)
    {
        take = Math.Clamp(take, 1, MaxPage);
        var query = db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrEmpty(userId))
        {
            query = query.Where(a => a.UserId == userId);
        }

        // Id зростає разом з часом запису, а SQLite не впорядковує DateTimeOffset.
        var entries = await query.OrderByDescending(a => a.Id).Take(take)
            .Select(a => new AuditRecord(a.Id, a.OccurredAt, a.Action, a.UserId, a.ActorId, a.IpAddress, a.Detail))
            .ToArrayAsync(cancellationToken);
        return TypedResults.Ok(entries);
    }
}
