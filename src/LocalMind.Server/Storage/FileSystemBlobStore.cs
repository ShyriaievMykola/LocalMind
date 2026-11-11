using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace LocalMind.Server.Storage;

/// <summary>Параметри сховища бекапів (секція <c>Storage</c>).</summary>
public sealed class BlobStoreOptions
{
    public const string SectionName = "Storage";

    /// <summary>Тека з файлами бекапів; відносний шлях — від кореня вмісту сервера.</summary>
    [Required]
    public string RootPath { get; set; } = "backups";

    /// <summary>Максимальний розмір одного бекапу в байтах.</summary>
    [Range(1, long.MaxValue)]
    public long MaxBackupBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>Скільки бекапів може зберігати один користувач (DR-032).</summary>
    [Range(1, 10_000)]
    public int MaxBackupsPerUser { get; set; } = 20;

    /// <summary>Сумарний розмір бекапів одного користувача в байтах (DR-032).</summary>
    [Range(1, long.MaxValue)]
    public long MaxTotalBytesPerUser { get; set; } = 1024L * 1024 * 1024;
}

/// <summary>Бекап більший за <see cref="BlobStoreOptions.MaxBackupBytes"/>.</summary>
public sealed class BlobTooLargeException : Exception
{
    public BlobTooLargeException()
        : base("Бекап перевищує допустимий розмір.")
    {
    }

    public BlobTooLargeException(string message)
        : base(message)
    {
    }

    public BlobTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Зберігає зашифровані бекапи файлами (SV-07). Імена файлів генерує сервер з <see cref="Guid"/>,
/// а будь-який шлях додатково перевіряється, щоб не вийти за межі кореневої теки.
/// </summary>
public sealed class FileSystemBlobStore
{
    private const string Extension = ".lmbk";
    private readonly string _root;
    private readonly long _maxBytes;

    public FileSystemBlobStore(IOptions<BlobStoreOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        // Без хвостового роздільника: інакше «/data/backups/» не збігся б з Path.GetDirectoryName у ResolvePath.
        _root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath)));
        _maxBytes = options.Value.MaxBackupBytes;
        Directory.CreateDirectory(_root);
    }

    public long MaxBytes => _maxBytes;

    /// <summary>
    /// Записує потік у файл. Спочатку пише в тимчасовий файл і лише потім перейменовує, тож обірване
    /// або завелике завантаження не лишає напівзаписаного бекапу.
    /// </summary>
    /// <returns>Розмір і SHA-256 (hex) записаних даних.</returns>
    /// <exception cref="BlobTooLargeException">Потік довший за ліміт.</exception>
    public async Task<(long Size, string Sha256)> WriteAsync(Guid id, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = ResolvePath(FileName(id));
        var temp = ResolvePath($"{Guid.NewGuid():N}.tmp");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long size = 0;
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 0, useAsync: true))
            {
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += read;
                    if (size > _maxBytes)
                    {
                        throw new BlobTooLargeException();
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            File.Move(temp, path);
        }
        finally
        {
            File.Delete(temp);
        }

        return (size, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    /// <summary>Відкриває бекап для читання або повертає <see langword="null"/>, якщо файла немає.</summary>
    public Stream? OpenRead(Guid id)
    {
        try
        {
            // FileShare.Delete: видалення бекапу під час чийогось скачування не падає (особливо на Windows).
            return new FileStream(ResolvePath(FileName(id)), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 0, useAsync: true);
        }
        catch (FileNotFoundException)
        {
            // Без окремого File.Exists: файл могли видалити між перевіркою і відкриттям.
            return null;
        }
    }

    /// <summary>Видаляє бекап; відсутній файл — не помилка.</summary>
    public void Delete(Guid id) => File.Delete(ResolvePath(FileName(id)));

    /// <summary>Повний шлях до файла в кореневій теці.</summary>
    /// <exception cref="ArgumentException">Ім'я веде за межі кореневої теки або містить підтеки.</exception>
    internal string ResolvePath(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(_root, fileName));
        if (!string.Equals(Path.GetDirectoryName(path), _root, StringComparison.Ordinal))
        {
            throw new ArgumentException("Недопустиме ім'я файла бекапу.", nameof(fileName));
        }

        return path;
    }

    private static string FileName(Guid id) => id.ToString("N") + Extension;
}
