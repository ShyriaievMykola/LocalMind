using System.ComponentModel.DataAnnotations;
using System.Text;

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

    /// <summary>Секретний ключ HMAC-SHA256, щонайменше 32 байти в UTF-8.</summary>
    public string SigningKey { get; set; } = string.Empty;

    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < MinKeyBytes)
        {
            yield return new ValidationResult(
                $"Ключ Jwt:SigningKey не задано або він коротший за {MinKeyBytes} байти. " +
                "Задайте його через `dotnet user-secrets` або змінну оточення Jwt__SigningKey.",
                [nameof(SigningKey)]);
        }
    }
}
