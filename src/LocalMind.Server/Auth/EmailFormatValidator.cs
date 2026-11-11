using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace LocalMind.Server.Auth;

/// <summary>
/// Строга перевірка email. Стандартний валідатор Identity шукає лише «@», а з вимкненим
/// <c>AllowedUserNameCharacters</c> пройшли б адреси з пробілами, керівними символами чи <c>&lt;&gt;</c>.
/// </summary>
public sealed class EmailFormatValidator : IUserValidator<IdentityUser>
{
    /// <summary>Email без зайвих пробілів у Unicode NFC — щоб «й» у NFC і NFD не давала два різні акаунти.</summary>
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Чи є рядок простою адресою <c>local@domain</c> без імені, пробілів і службових символів.</summary>
    public static bool IsValid(string? email) =>
        !string.IsNullOrEmpty(email)
        && !email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>' or '"' or '(' or ')' or ',' or ';')
        && MailAddress.TryCreate(email, out var address)
        && string.Equals(address.Address, email, StringComparison.Ordinal)
        && address.Host.Contains('.', StringComparison.Ordinal);

    /// <inheritdoc />
    public Task<IdentityResult> ValidateAsync(UserManager<IdentityUser> manager, IdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult(IsValid(user.Email)
            ? IdentityResult.Success
            : IdentityResult.Failed(manager.ErrorDescriber.InvalidEmail(user.Email)));
    }
}
