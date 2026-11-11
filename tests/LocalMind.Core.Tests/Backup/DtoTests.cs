using LocalMind.Core.Backup.Dtos;

namespace LocalMind.Core.Tests.Backup;

public sealed class DtoTests
{
    private const string Secret = "СЕКРЕТ-123";

    public static TheoryData<object> SecretCarriers => new()
    {
        new RegisterRequest("a@example.com", Secret),
        new LoginRequest("a@example.com", Secret),
        new RefreshRequest(Secret),
        new LogoutRequest(Secret),
        new TokenResponse(Secret, DateTimeOffset.UnixEpoch, Secret, DateTimeOffset.UnixEpoch),
    };

    [Theory]
    [MemberData(nameof(SecretCarriers))]
    public void ToString_DoesNotRevealSecrets(object dto)
    {
        var text = dto.ToString()!;

        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        Assert.Contains("***", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Records_KeepValueEquality()
    {
        Assert.Equal(new LoginRequest("a@example.com", Secret), new LoginRequest("a@example.com", Secret));
        Assert.NotEqual(new LoginRequest("a@example.com", Secret), new LoginRequest("a@example.com", "інший"));
    }
}
