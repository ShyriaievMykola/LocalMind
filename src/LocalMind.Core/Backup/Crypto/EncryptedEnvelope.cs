using System.Buffers.Binary;
using LocalMind.Core.Errors;

namespace LocalMind.Core.Backup.Crypto;

/// <summary>
/// Зашифрований бекап у форматі v1 (DR-014):
/// <c>"LMBK" | version (1) | salt (16) | nonce (12) | ciphertext | tag (16)</c>.
/// Заголовок (усе до шифротексту) автентифікується як associated data, тож його зміна теж виявляється.
/// </summary>
public sealed class EncryptedEnvelope
{
    /// <summary>Поточна версія формату.</summary>
    public const byte CurrentVersion = 1;

    /// <summary>Розмір nonce AES-GCM у байтах.</summary>
    public const int NonceSize = 12;

    /// <summary>Розмір тегу автентифікації AES-GCM у байтах.</summary>
    public const int TagSize = 16;

    /// <summary>Розмір заголовка: magic + version + salt + nonce.</summary>
    public const int HeaderSize = MagicSize + 1 + KeyDerivation.SaltSize + NonceSize;

    private const int MagicSize = 4;
    private const uint Magic = 0x4C4D424B; // "LMBK"

    /// <param name="version">Версія формату.</param>
    /// <param name="salt">Сіль PBKDF2.</param>
    /// <param name="nonce">Nonce AES-GCM.</param>
    /// <param name="ciphertext">Шифротекст.</param>
    /// <param name="tag">Тег автентифікації.</param>
    /// <exception cref="ArgumentException">Неправильний розмір солі, nonce чи тегу.</exception>
    public EncryptedEnvelope(byte version, ReadOnlyMemory<byte> salt, ReadOnlyMemory<byte> nonce,
        ReadOnlyMemory<byte> ciphertext, ReadOnlyMemory<byte> tag)
    {
        if (salt.Length != KeyDerivation.SaltSize)
        {
            throw new ArgumentException($"Сіль має бути {KeyDerivation.SaltSize} байт.", nameof(salt));
        }

        if (nonce.Length != NonceSize)
        {
            throw new ArgumentException($"Nonce має бути {NonceSize} байт.", nameof(nonce));
        }

        if (tag.Length != TagSize)
        {
            throw new ArgumentException($"Тег має бути {TagSize} байт.", nameof(tag));
        }

        Version = version;
        Salt = salt;
        Nonce = nonce;
        Ciphertext = ciphertext;
        Tag = tag;
    }

    public byte Version { get; }

    public ReadOnlyMemory<byte> Salt { get; }

    public ReadOnlyMemory<byte> Nonce { get; }

    public ReadOnlyMemory<byte> Ciphertext { get; }

    public ReadOnlyMemory<byte> Tag { get; }

    /// <summary>Серіалізує конверт у байти.</summary>
    public byte[] ToBytes()
    {
        var result = new byte[HeaderSize + Ciphertext.Length + TagSize];
        WriteHeader(result, Version, Salt.Span, Nonce.Span);
        Ciphertext.Span.CopyTo(result.AsSpan(HeaderSize));
        Tag.Span.CopyTo(result.AsSpan(HeaderSize + Ciphertext.Length));
        return result;
    }

    /// <summary>Розбирає серіалізований конверт. Дані не копіюються — конверт посилається на <paramref name="data"/>.</summary>
    /// <exception cref="BackupDecryptionException">Не формат LocalMind, обрізаний файл або невідома версія.</exception>
    public static EncryptedEnvelope Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < HeaderSize + TagSize || BinaryPrimitives.ReadUInt32BigEndian(span) != Magic)
        {
            throw new BackupDecryptionException("Файл не є бекапом LocalMind або пошкоджений.");
        }

        var version = span[MagicSize];
        if (version != CurrentVersion)
        {
            throw new BackupDecryptionException($"Непідтримувана версія формату бекапу: {version}.");
        }

        const int saltOffset = MagicSize + 1;
        var salt = data.Slice(saltOffset, KeyDerivation.SaltSize);
        var nonce = data.Slice(saltOffset + KeyDerivation.SaltSize, NonceSize);
        var ciphertext = data[HeaderSize..^TagSize];
        var tag = data[^TagSize..];
        return new EncryptedEnvelope(version, salt, nonce, ciphertext, tag);
    }

    /// <summary>Заголовок, що автентифікується як associated data.</summary>
    internal byte[] GetHeader()
    {
        var header = new byte[HeaderSize];
        WriteHeader(header, Version, Salt.Span, Nonce.Span);
        return header;
    }

    internal static void WriteHeader(Span<byte> destination, byte version, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> nonce)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, Magic);
        destination[MagicSize] = version;
        salt.CopyTo(destination[(MagicSize + 1)..]);
        nonce.CopyTo(destination[(MagicSize + 1 + KeyDerivation.SaltSize)..]);
    }
}
