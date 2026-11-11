using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using LocalMind.Core.Backup.Dtos;

namespace LocalMind.Core.Backup;

/// <summary>Де клієнт тримає пару токенів між запитами (B-06, DR-031).</summary>
public interface ITokenStore
{
    /// <summary>Поточні токени або <see langword="null"/>, якщо користувач не увійшов.</summary>
    TokenResponse? Load();

    void Save(TokenResponse tokens);

    void Clear();
}

/// <summary>Токени лише в пам'яті процесу: після перезапуску треба увійти знову.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly Lock _lock = new();
    private TokenResponse? _tokens;

    /// <inheritdoc />
    public TokenResponse? Load()
    {
        lock (_lock)
        {
            return _tokens;
        }
    }

    /// <inheritdoc />
    public void Save(TokenResponse tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        lock (_lock)
        {
            _tokens = tokens;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _tokens = null;
        }
    }
}

/// <summary>
/// Windows: сесія переживає перезапуск, файл зашифровано DPAPI (ключ прив'язаний до облікового запису Windows),
/// тож інший користувач чи копія файла на іншу машину токен не розкриють.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiTokenStore(string filePath) : ITokenStore
{
    // Додаткова ентропія відокремлює наші дані від інших DPAPI-блобів того самого користувача.
    private static readonly byte[] Entropy = "LocalMind.Session.v1"u8.ToArray();

    private readonly Lock _lock = new();
    private TokenResponse? _cached;
    private bool _loaded;

    /// <inheritdoc />
    public TokenResponse? Load()
    {
        lock (_lock)
        {
            if (!_loaded)
            {
                _cached = ReadFile();
                _loaded = true;
            }

            return _cached;
        }
    }

    /// <inheritdoc />
    public void Save(TokenResponse tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        lock (_lock)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(tokens);
            try
            {
                var protectedBytes = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
                var temp = filePath + ".tmp";
                File.WriteAllBytes(temp, protectedBytes);
                File.Move(temp, filePath, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }

            _cached = tokens;
            _loaded = true;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            TryDelete(filePath);
            _cached = null;
            _loaded = true;
        }
    }

    private TokenResponse? ReadFile()
    {
        byte[] protectedBytes;
        try
        {
            protectedBytes = File.ReadAllBytes(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Файла чи теки немає, або файл недоступний — вважаємо, що сесії немає.
            return null;
        }

        try
        {
            var json = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return JsonSerializer.Deserialize<TokenResponse>(json);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // Пошкоджений файл або чужий обліковий запис Windows — сесії просто немає.
            TryDelete(filePath);
            return null;
        }
    }

    /// <summary>Видаляє файл сесії; відсутність файла чи теки — не помилка.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // Теки немає — і файла теж.
        }
    }
}
