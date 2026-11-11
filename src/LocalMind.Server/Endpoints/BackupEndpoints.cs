using System.Security.Claims;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Server.Data;
using LocalMind.Server.Data.Entities;
using LocalMind.Server.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LocalMind.Server.Endpoints;

/// <summary>
/// Завантаження, список, скачування й видалення бекапів (SV-06). Користувач бачить лише власні бекапи:
/// чужий для нього не існує (404). Сервер отримує й віддає лише шифротекст.
/// </summary>
public static class BackupEndpoints
{
    private const string OctetStream = "application/octet-stream";

    public static RouteGroupBuilder MapBackupEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var backups = api.MapGroup("/backups").WithTags("Backups").RequireAuthorization();
        backups.MapPost("/", UploadAsync).Accepts<Stream>(OctetStream);
        backups.MapGet("/", ListAsync);
        backups.MapGet("/{id:guid}", DownloadAsync);
        backups.MapDelete("/{id:guid}", DeleteAsync);
        return api;
    }

    private static async Task<Results<Created<BackupInfo>, ProblemHttpResult, ChallengeHttpResult>> UploadAsync(
        HttpContext context, ClaimsPrincipal principal, ServerDbContext db, FileSystemBlobStore store,
        IOptions<BlobStoreOptions> options, TimeProvider time, CancellationToken cancellationToken)
    {
        // Токен ще чинний, а користувача видалено — інакше впали б на зовнішньому ключі з 500.
        var userId = UserId(principal);
        if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return TypedResults.Challenge();
        }

        if (context.Request.ContentLength > store.MaxBytes)
        {
            return TooLarge(store);
        }

        var quota = options.Value;
        var own = db.Snapshots.Where(s => s.UserId == userId);
        var usedCount = await own.CountAsync(cancellationToken);
        var usedBytes = await own.SumAsync(s => s.SizeBytes, cancellationToken);
        if (usedCount >= quota.MaxBackupsPerUser
            || usedBytes + (context.Request.ContentLength ?? 0) > quota.MaxTotalBytesPerUser)
        {
            return QuotaExceeded(quota);
        }

        // Ліміт Kestrel за замовчуванням (30 МБ) замінюємо своїм; точний контроль — під час запису.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = store.MaxBytes + 1;
        }

        var id = Guid.NewGuid();
        long size;
        string sha256;
        try
        {
            (size, sha256) = await store.WriteAsync(id, context.Request.Body, cancellationToken);
        }
        catch (Exception ex) when (ex is BlobTooLargeException or BadHttpRequestException { StatusCode: 413 })
        {
            return TooLarge(store);
        }

        // Без Content-Length розмір відомий лише після запису. Паралельні завантаження можуть разом трохи
        // перевищити квоту — це прийнятно: мета квоти — не дати заповнити диск, а не точний облік (DR-032).
        if (usedBytes + size > quota.MaxTotalBytesPerUser)
        {
            store.Delete(id);
            return QuotaExceeded(quota);
        }

        var snapshot = new SnapshotEntity
        {
            Id = id,
            UserId = userId,
            SizeBytes = size,
            Sha256 = sha256,
            CreatedAt = time.GetUtcNow(),
        };
        db.Add(snapshot);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            store.Delete(id);
            throw;
        }

        return TypedResults.Created($"/api/v1/backups/{id}", ToInfo(snapshot));
    }

    private static async Task<Ok<BackupInfo[]>> ListAsync(
        ClaimsPrincipal principal, ServerDbContext db, CancellationToken cancellationToken)
    {
        var userId = UserId(principal);
        var snapshots = await db.Snapshots.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);

        // SQLite не впорядковує DateTimeOffset на боці БД, а бекапів у користувача небагато.
        return TypedResults.Ok(snapshots.OrderByDescending(s => s.CreatedAt).Select(ToInfo).ToArray());
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadAsync(
        Guid id, ClaimsPrincipal principal, ServerDbContext db, FileSystemBlobStore store,
        CancellationToken cancellationToken)
    {
        var snapshot = await FindOwnAsync(id, principal, db, cancellationToken);
        var stream = snapshot is null ? null : store.OpenRead(id);
        return stream is null
            ? TypedResults.NotFound()
            : TypedResults.File(stream, OctetStream, $"localmind-{id:N}.lmbk");
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id, ClaimsPrincipal principal, ServerDbContext db, FileSystemBlobStore store,
        CancellationToken cancellationToken)
    {
        var snapshot = await FindOwnAsync(id, principal, db, cancellationToken);
        if (snapshot is null)
        {
            return TypedResults.NotFound();
        }

        db.Remove(snapshot);
        await db.SaveChangesAsync(cancellationToken);
        store.Delete(id);
        return TypedResults.NoContent();
    }

    private static Task<SnapshotEntity?> FindOwnAsync(
        Guid id, ClaimsPrincipal principal, ServerDbContext db, CancellationToken cancellationToken)
    {
        var userId = UserId(principal);
        return db.Snapshots.SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.Identity?.Name ?? throw new InvalidOperationException("У токені немає ідентифікатора користувача.");

    private static BackupInfo ToInfo(SnapshotEntity s) => new(s.Id, s.SizeBytes, s.Sha256, s.CreatedAt);

    private static ProblemHttpResult TooLarge(FileSystemBlobStore store) => TypedResults.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: $"Бекап перевищує допустимий розмір ({store.MaxBytes / (1024 * 1024)} МБ).");

    private static ProblemHttpResult QuotaExceeded(BlobStoreOptions quota) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Вичерпано місце для бекапів. Видаліть старі бекапи.",
        detail: $"Ліміт: {quota.MaxBackupsPerUser} бекапів і {quota.MaxTotalBytesPerUser / (1024 * 1024)} МБ разом.");
}
