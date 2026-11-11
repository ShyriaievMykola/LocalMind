using System.Security.Cryptography;
using System.Text;
using LocalMind.Server.Data;
using LocalMind.Server.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LocalMind.Server.Auth;

/// <summary>
/// Refresh-токени (SV-05, DR-018): у БД лише хеш, кожне оновлення видає новий токен і гасить старий,
/// повторне пред'явлення вже використаного токена відкликає всю сесію. Сесія також стає недійсною, якщо змінився
/// SecurityStamp користувача (зміна пароля), акаунт заблоковано або минув абсолютний строк сесії.
/// </summary>
public sealed class RefreshTokenService(ServerDbContext db, IOptions<JwtOptions> options, TimeProvider time)
{
    private const int TokenBytes = 32;

    /// <summary>Новий токен для нової сесії (після входу).</summary>
    public Task<(string Token, DateTimeOffset ExpiresAt)> IssueAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return IssueAsync(user, Guid.NewGuid(), time.GetUtcNow(), cancellationToken);
    }

    /// <summary>
    /// Обмінює чинний токен на новий у тій самій сесії. Повертає <see langword="null"/>, якщо токен невідомий,
    /// прострочений, відкликаний, вже використаний, або користувач змінив пароль чи заблокований
    /// (в останніх випадках сесію відкликано).
    /// </summary>
    public async Task<(IdentityUser User, string Token, DateTimeOffset ExpiresAt)?> RotateAsync(
        string token, CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(token, cancellationToken);
        if (stored is null || stored.RevokedAt is not null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (stored.ConsumedAt is not null)
        {
            await RevokeSessionAsync(stored.SessionId, cancellationToken);
            return null;
        }

        if (stored.ExpiresAt <= now || stored.SessionStartedAt + options.Value.SessionMaxLifetime <= now)
        {
            return null;
        }

        // Атомарно «гасимо» токен: з двох одночасних запитів з тим самим токеном виграє лише один,
        // другий виглядає як повторне використання.
        var consumed = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.ConsumedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);
        if (consumed == 0)
        {
            await RevokeSessionAsync(stored.SessionId, cancellationToken);
            return null;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);
        if (user is null || user.SecurityStamp != stored.SecurityStamp || user.LockoutEnd > now)
        {
            await RevokeSessionAsync(stored.SessionId, cancellationToken);
            return null;
        }

        var (next, expiresAt) = await IssueAsync(user, stored.SessionId, stored.SessionStartedAt, cancellationToken);
        return (user, next, expiresAt);
    }

    /// <summary>Вихід: відкликає сесію, до якої належить токен. Невідомий токен ігнорується.</summary>
    public async Task RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(token, cancellationToken);
        if (stored is not null)
        {
            await RevokeSessionAsync(stored.SessionId, cancellationToken);
        }
    }

    /// <summary>Відкликає всі сесії користувача — для зміни пароля чи підозри на компрометацію.</summary>
    public async Task RevokeAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }

    internal static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<(string Token, DateTimeOffset ExpiresAt)> IssueAsync(
        IdentityUser user, Guid sessionId, DateTimeOffset sessionStartedAt, CancellationToken cancellationToken)
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = time.GetUtcNow();
        var sessionEnd = sessionStartedAt + options.Value.SessionMaxLifetime;
        var expiresAt = now.Add(options.Value.RefreshTokenLifetime);
        expiresAt = expiresAt < sessionEnd ? expiresAt : sessionEnd;
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SessionId = sessionId,
            SessionStartedAt = sessionStartedAt,
            SecurityStamp = user.SecurityStamp,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        return (token, expiresAt);
    }

    private async Task<RefreshTokenEntity?> FindAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = Hash(token);
        return await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
    }

    private async Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.RefreshTokens
            .Where(t => t.SessionId == sessionId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }
}
