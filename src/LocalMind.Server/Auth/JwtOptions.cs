using System.ComponentModel.DataAnnotations;

namespace LocalMind.Server.Auth;

/// <summary>
/// Параметри access-токенів (секція <c>Jwt</c>). Ключ підпису — лише з user-secrets або змінної оточення
/// <c>Jwt__SigningKey</c>, у репозиторії його немає (DR-017).
/// </summary>
public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";

    /// <summary>Мінімальна довжина ключа HMAC-SHA256 у байтах.</summary>
    public const int MinKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Секретний ключ HMAC-SHA256 у base64, щонайменше 32 байти після декодування.</summary>
    public string SigningKey { get; set; } = string.Empty;

    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Строк дії refresh-токена (і максимальна тривалість простою сесії).</summary>
    [Range(typeof(TimeSpan), "01:00:00", "90.00:00:00")]
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TryGetKeyBytes() is not { Length: >= MinKeyBytes })
        {
            yield return new ValidationResult(
                $"Ключ Jwt:SigningKey не задано, він не base64 або коротший за {MinKeyBytes} байти. " +
                "Згенеруйте його (`openssl rand -base64 48`) і задайте через `dotnet user-secrets` " +
                "або змінну оточення Jwt__SigningKey.",
                [nameof(SigningKey)]);
        }
    }

    /// <summary>Байти ключа; викликати лише після валідації опцій.</summary>
    internal byte[] GetKeyBytes() => TryGetKeyBytes() ?? throw new InvalidOperationException("Jwt:SigningKey не валідний.");

    private byte[]? TryGetKeyBytes()
    {
        var buffer = new byte[SigningKey.Length];
        return Convert.TryFromBase64String(SigningKey, buffer, out var written) ? buffer[..written] : null;
    }
}
