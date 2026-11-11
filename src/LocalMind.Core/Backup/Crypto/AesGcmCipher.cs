using System.Buffers;
using System.Security.Cryptography;
using LocalMind.Core.Errors;

namespace LocalMind.Core.Backup.Crypto;

/// <summary>
/// Шифрування бекапу на клієнті: ключ із пароля (<see cref="KeyDerivation"/>) + AES-256-GCM (DR-008, DR-014).
/// Кожне шифрування бере нові випадкові сіль і nonce, тож пара (ключ, nonce) не повторюється.
/// </summary>
public static class AesGcmCipher
{
    /// <summary>Шифрує дані паролем.</summary>
    /// <exception cref="ArgumentException">Порожній пароль.</exception>
    public static EncryptedEnvelope Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<char> password)
    {
        var salt = KeyDerivation.CreateSalt();
        var nonce = RandomNumberGenerator.GetBytes(EncryptedEnvelope.NonceSize);
        var header = new byte[EncryptedEnvelope.HeaderSize];
        EncryptedEnvelope.WriteHeader(header, EncryptedEnvelope.CurrentVersion, salt, nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[EncryptedEnvelope.TagSize];
        var key = KeyDerivation.DeriveKey(password, salt);
        try
        {
            using var aes = new AesGcm(key, EncryptedEnvelope.TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return new EncryptedEnvelope(EncryptedEnvelope.CurrentVersion, salt, nonce, ciphertext, tag);
    }

    /// <summary>Розшифровує конверт і перевіряє тег.</summary>
    /// <exception cref="ArgumentException">Порожній пароль.</exception>
    /// <exception cref="BackupDecryptionException">Неправильний пароль або дані змінено.</exception>
    public static byte[] Decrypt(EncryptedEnvelope envelope, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var plaintext = new byte[envelope.Ciphertext.Length];
        var key = KeyDerivation.DeriveKey(password, envelope.Salt.Span);
        try
        {
            using var aes = new AesGcm(key, EncryptedEnvelope.TagSize);
            aes.Decrypt(envelope.Nonce.Span, envelope.Ciphertext.Span, envelope.Tag.Span, plaintext, envelope.GetHeader());
            return plaintext;
        }
        catch (AuthenticationTagMismatchException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new BackupDecryptionException(BackupDecryptionException.DefaultMessage, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Шифрує потік у потік. AES-GCM у .NET не потоковий, тому вхід повністю читається в пам'ять —
    /// розмір бекапу обмежений лімітом сервера (DR-014). PBKDF2 (~0,5 с) іде в пулі потоків, не блокуючи UI.
    /// Усі проміжні буфери з відкритим текстом затираються.
    /// </summary>
    /// <exception cref="ArgumentException">Порожній пароль.</exception>
    public static async Task EncryptAsync(Stream input, Stream output, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var (buffer, length) = await ReadAllAsync(input, cancellationToken).ConfigureAwait(false);
        try
        {
            var envelope = await Task.Run(() => Encrypt(buffer.AsSpan(0, length), password), cancellationToken)
                .ConfigureAwait(false);
            await output.WriteAsync(envelope.ToBytes(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Розшифровує потік у потік. Якщо перевірка тегу не пройшла, у вихід нічого не пишеться.</summary>
    /// <exception cref="ArgumentException">Порожній пароль.</exception>
    /// <exception cref="BackupDecryptionException">Неправильний пароль, дані змінено або не формат LocalMind.</exception>
    public static async Task DecryptAsync(Stream input, Stream output, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var (buffer, length) = await ReadAllAsync(input, cancellationToken).ConfigureAwait(false);
        byte[] plaintext;
        try
        {
            var data = buffer.AsMemory(0, length);
            plaintext = await Task.Run(() => Decrypt(EncryptedEnvelope.Parse(data), password), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        try
        {
            await output.WriteAsync(plaintext, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Читає потік у буфер з <see cref="ArrayPool{T}"/>. На відміну від <see cref="MemoryStream"/>, при зростанні
    /// старий буфер затирається, тож відкритий текст не лишається в покинутих масивах. Буфер повертає викликач.
    /// </summary>
    private static async Task<(byte[] Buffer, int Length)> ReadAllAsync(Stream input, CancellationToken cancellationToken)
    {
        var initial = input.CanSeek ? (int)Math.Clamp(input.Length - input.Position + 1, 1, Array.MaxLength) : 81920;
        var buffer = ArrayPool<byte>.Shared.Rent(initial);
        var length = 0;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                length += read;
                if (length == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(checked(buffer.Length * 2));
                    buffer.AsSpan(0, length).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                    buffer = larger;
                }
            }

            return (buffer, length);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            throw;
        }
    }
}
