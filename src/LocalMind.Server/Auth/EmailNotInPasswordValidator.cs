using Microsoft.AspNetCore.Identity;

namespace LocalMind.Server.Auth;

/// <summary>Відкидає пароль, що містить email або його локальну частину (контекстні слова, NIST SP 800-63B).</summary>
public sealed class EmailNotInPasswordValidator : IPasswordValidator<IdentityUser>
{
    internal const string Code = "PasswordContainsEmail";

    /// <summary>Коротші локальні частини (<c>a@…</c>) не перевіряємо — збіг з ними випадковий.</summary>
    private const int MinLocalPartLength = 3;

    /// <inheritdoc />
    public Task<IdentityResult> ValidateAsync(UserManager<IdentityUser> manager, IdentityUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);

        var email = user.Email ?? string.Empty;
        var at = email.IndexOf('@', StringComparison.Ordinal);
        var localPart = at > 0 ? email[..at] : email;
        var contains = !string.IsNullOrEmpty(password) && localPart.Length >= MinLocalPartLength
            && password.Contains(localPart, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(contains
            ? IdentityResult.Failed(new IdentityError { Code = Code, Description = "Пароль не може містити email." })
            : IdentityResult.Success);
    }
}
