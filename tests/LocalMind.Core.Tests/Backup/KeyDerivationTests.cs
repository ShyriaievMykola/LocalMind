using System.Text;
using LocalMind.Core.Backup.Crypto;

namespace LocalMind.Core.Tests.Backup;

public sealed class KeyDerivationTests
{
    [Fact]
    public void DeriveKey_SamePasswordAndSalt_ReturnsSameKey()
    {
        var salt = KeyDerivation.CreateSalt();

        var first = KeyDerivation.DeriveKey("пароль", salt);
        var second = KeyDerivation.DeriveKey("пароль", salt);

        Assert.Equal(KeyDerivation.KeySize, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DeriveKey_DifferentSalt_ReturnsDifferentKey()
    {
        var first = KeyDerivation.DeriveKey("пароль", KeyDerivation.CreateSalt());
        var second = KeyDerivation.DeriveKey("пароль", KeyDerivation.CreateSalt());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DeriveKey_DifferentPassword_ReturnsDifferentKey()
    {
        var salt = KeyDerivation.CreateSalt();

        Assert.NotEqual(KeyDerivation.DeriveKey("пароль", salt), KeyDerivation.DeriveKey("Пароль", salt));
    }

    [Fact]
    public void Parameters_MeetOwaspMinimum()
    {
        Assert.True(KeyDerivation.Iterations >= 600_000);
        Assert.True(KeyDerivation.SaltSize >= 16);
        Assert.Equal(32, KeyDerivation.KeySize);
    }

    [Fact]
    public void CreateSalt_ReturnsRandomBytes()
    {
        Assert.NotEqual(KeyDerivation.CreateSalt(), KeyDerivation.CreateSalt());
    }

    [Fact]
    public void DeriveKey_InvalidArguments_Throws()
    {
        Assert.Throws<ArgumentException>(() => KeyDerivation.DeriveKey("", KeyDerivation.CreateSalt()));
        Assert.Throws<ArgumentException>(() => KeyDerivation.DeriveKey("пароль", new byte[8]));
    }

    [Fact]
    public void DeriveKey_NfcAndNfdPassword_ReturnSameKey()
    {
        var salt = KeyDerivation.CreateSalt();
        var nfc = "пароль-й".Normalize(NormalizationForm.FormC);
        var nfd = nfc.Normalize(NormalizationForm.FormD);

        Assert.NotEqual(nfc, nfd, StringComparer.Ordinal);
        Assert.Equal(KeyDerivation.DeriveKey(nfc, salt), KeyDerivation.DeriveKey(nfd, salt));
    }
}
