using System.Text;

namespace LocalMind.Core.Backup.Dtos;

// Секрети маскуються в ToString(), щоб випадковий лог моделі не розкрив пароль чи токен.

/// <summary>Запит на реєстрацію на сервері бекапу.</summary>
/// <param name="Email">Email — він же логін.</param>
/// <param name="Password">Пароль облікового запису (не пароль шифрування бекапу).</param>
public sealed record RegisterRequest(string Email, string Password)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Email = ").Append(Email).Append(", Password = ***");
        return true;
    }
}

/// <summary>Запит на вхід.</summary>
/// <param name="Email">Email.</param>
/// <param name="Password">Пароль облікового запису.</param>
public sealed record LoginRequest(string Email, string Password)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Email = ").Append(Email).Append(", Password = ***");
        return true;
    }
}

/// <summary>Запит на оновлення пари токенів.</summary>
/// <param name="RefreshToken">Чинний refresh-токен.</param>
public sealed record RefreshRequest(string RefreshToken)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("RefreshToken = ").Append(string.IsNullOrEmpty(RefreshToken) ? string.Empty : "***");
        return true;
    }
}

/// <summary>Запит на вихід — відкликає сесію, до якої належить токен.</summary>
/// <param name="RefreshToken">Refresh-токен сесії.</param>
public sealed record LogoutRequest(string RefreshToken)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("RefreshToken = ").Append(string.IsNullOrEmpty(RefreshToken) ? string.Empty : "***");
        return true;
    }
}

/// <summary>Пара токенів після входу або оновлення.</summary>
/// <param name="AccessToken">Короткоживучий JWT для заголовка <c>Authorization: Bearer</c>.</param>
/// <param name="AccessTokenExpiresAt">Коли спливає access-токен.</param>
/// <param name="RefreshToken">Одноразовий refresh-токен.</param>
/// <param name="RefreshTokenExpiresAt">Коли спливає refresh-токен.</param>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("AccessToken = ***, AccessTokenExpiresAt = ").Append(AccessTokenExpiresAt)
            .Append(", RefreshToken = ***, RefreshTokenExpiresAt = ").Append(RefreshTokenExpiresAt);
        return true;
    }
}

/// <summary>Поточний користувач — відповідь на <c>GET /api/v1/auth/me</c>.</summary>
/// <param name="Id">Ідентифікатор користувача на сервері.</param>
/// <param name="Email">Email.</param>
public sealed record UserInfo(string Id, string Email);
