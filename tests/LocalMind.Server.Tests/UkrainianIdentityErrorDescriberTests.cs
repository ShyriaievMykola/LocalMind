using LocalMind.Server.Auth;
using Microsoft.AspNetCore.Identity;

namespace LocalMind.Server.Tests;

public class UkrainianIdentityErrorDescriberTests
{
    private readonly UkrainianIdentityErrorDescriber _describer = new();

    [Fact]
    public void EveryMessage_IsUkrainianAndKeepsIdentityErrorCode()
    {
        var errors = new Dictionary<string, IdentityError>
        {
            [nameof(IdentityErrorDescriber.DuplicateEmail)] = _describer.DuplicateEmail("a@b.c"),
            [nameof(IdentityErrorDescriber.DuplicateUserName)] = _describer.DuplicateUserName("a@b.c"),
            [nameof(IdentityErrorDescriber.InvalidEmail)] = _describer.InvalidEmail("x"),
            [nameof(IdentityErrorDescriber.InvalidUserName)] = _describer.InvalidUserName("x"),
            [nameof(IdentityErrorDescriber.PasswordTooShort)] = _describer.PasswordTooShort(8),
            [nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars)] = _describer.PasswordRequiresUniqueChars(4),
            [nameof(IdentityErrorDescriber.PasswordRequiresDigit)] = _describer.PasswordRequiresDigit(),
            [nameof(IdentityErrorDescriber.PasswordRequiresLower)] = _describer.PasswordRequiresLower(),
            [nameof(IdentityErrorDescriber.PasswordRequiresUpper)] = _describer.PasswordRequiresUpper(),
            [nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric)] = _describer.PasswordRequiresNonAlphanumeric(),
        };

        foreach (var (code, error) in errors)
        {
            Assert.Equal(code, error.Code);
            Assert.Matches("[А-Яа-яІіЇїЄєҐґ]", error.Description);
            Assert.DoesNotContain("username", error.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DuplicateEmailAndUserName_GiveTheSameMessage_SoLoginIsNotRevealedTwice() =>
        Assert.Equal(_describer.DuplicateEmail("a@b.c").Description, _describer.DuplicateUserName("a@b.c").Description);

    [Fact]
    public void PasswordRules_MentionTheConfiguredNumbers()
    {
        Assert.Contains("12", _describer.PasswordTooShort(12).Description, StringComparison.Ordinal);
        Assert.Contains("5", _describer.PasswordRequiresUniqueChars(5).Description, StringComparison.Ordinal);
    }
}
