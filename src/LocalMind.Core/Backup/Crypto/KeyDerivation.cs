using System.Security.Cryptography;
using System.Text;

namespace LocalMind.Core.Backup.Crypto;

/// <summary>Виведення ключа шифрування з пароля: PBKDF2-HMAC-SHA256 (DR-008, DR-014).</summary>
public static class KeyDerivation
{
    /// <summary>Кількість ітерацій для формату v1 — рекомендація OWASP для PBKDF2-HMAC-SHA256.</summary>
    public const int Iterations = 600_000;

    /// <summary>Розмір солі в байтах.</summary>
    public const int SaltSize = 16;

    /// <summary>Розмір ключа в байтах (AES-256).</summary>
    public const int KeySize = 32;

    /// <summary>Нова криптографічно випадкова сіль.</summary>
    public static byte[] CreateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    /// <summary>
    /// Виводить 256-бітний ключ. Однакові пароль і сіль завжди дають однаковий ключ.
    /// Пароль нормалізується в Unicode NFC: та сама фраза, введена на macOS (часто NFD) і Windows, дає той самий ключ.
    /// </summary>
    /// <exception cref="ArgumentException">Порожній пароль або сіль неправильного розміру.</exception>
    public static byte[] DeriveKey(ReadOnlySpan<char> password, ReadOnlySpan<byte> salt)
    {
        if (password.IsEmpty)
        {
            throw new ArgumentException("Пароль шифрування не може бути порожнім.", nameof(password));
        }

        if (salt.Length != SaltSize)
        {
            throw new ArgumentException($"Сіль має бути {SaltSize} байт.", nameof(salt));
        }

        var text = password.ToString();
        var normalized = text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);
        return Rfc2898DeriveBytes.Pbkdf2(normalized, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
