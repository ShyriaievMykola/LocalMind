using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LocalMind.Server.Auth;

/// <summary>Видає короткоживучі access-токени (JWT, HS256) і задає правила їх перевірки (SV-04).</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    private readonly JwtOptions _options = options.Value;
    /// <summary>Тип claim з ролями в токені.</summary>
    public const string RoleClaimType = "role";

    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>Створює access-токен для користувача.</summary>
    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(IdentityUser user, IEnumerable<string>? roles = null)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = time.GetUtcNow();
        var expiresAt = now.Add(_options.AccessTokenLifetime);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateKey(_options), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                .. (roles ?? []).Select(role => new Claim(RoleClaimType, role)),
            ]),
        };
        return (_handler.CreateToken(descriptor), expiresAt);
    }

    /// <summary>Параметри перевірки: підпис, issuer, audience, строк дії; лише HS256.</summary>
    public static TokenValidationParameters CreateValidationParameters(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = CreateKey(options),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = RoleClaimType,
        };
    }

    private static SymmetricSecurityKey CreateKey(JwtOptions options) => new(options.GetKeyBytes());
}
