namespace LocalMind.Core.Backup.Dtos;

/// <summary>
/// Метадані збереженого бекапу. Сам бекап передається окремо як <c>application/octet-stream</c>
/// і для сервера є лише шифротекстом.
/// </summary>
/// <param name="Id">Ідентифікатор, який видав сервер.</param>
/// <param name="SizeBytes">Розмір зашифрованого файла.</param>
/// <param name="Sha256">SHA-256 зашифрованого файла (hex, нижній регістр) — для перевірки після завантаження.</param>
/// <param name="CreatedAt">Коли сервер прийняв бекап.</param>
public sealed record BackupInfo(Guid Id, long SizeBytes, string Sha256, DateTimeOffset CreatedAt);
