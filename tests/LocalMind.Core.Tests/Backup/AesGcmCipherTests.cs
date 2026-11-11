using System.Text;
using LocalMind.Core.Backup.Crypto;
using LocalMind.Core.Errors;

namespace LocalMind.Core.Tests.Backup;

public sealed class AesGcmCipherTests
{
    private const string Password = "правильний пароль";
    private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("нотатки для бекапу — особисті дані");

    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        var envelope = AesGcmCipher.Encrypt(Plaintext, Password);
        var parsed = EncryptedEnvelope.Parse(envelope.ToBytes());

        Assert.Equal(Plaintext, AesGcmCipher.Decrypt(parsed, Password));
    }

    [Fact]
    public void Encrypt_CiphertextDoesNotContainPlaintext()
    {
        var bytes = AesGcmCipher.Encrypt(Plaintext, Password).ToBytes();

        Assert.Equal(-1, bytes.AsSpan().IndexOf(Plaintext.AsSpan(0, 16)));
    }

    [Fact]
    public void EncryptDecrypt_EmptyPlaintext_RoundTrips()
    {
        var bytes = AesGcmCipher.Encrypt([], Password).ToBytes();

        Assert.Equal(EncryptedEnvelope.HeaderSize + EncryptedEnvelope.TagSize, bytes.Length);
        Assert.Empty(AesGcmCipher.Decrypt(EncryptedEnvelope.Parse(bytes), Password));
    }

    [Fact]
    public void Decrypt_WrongPassword_Throws()
    {
        var envelope = AesGcmCipher.Encrypt(Plaintext, Password);

        Assert.Throws<BackupDecryptionException>(() => AesGcmCipher.Decrypt(envelope, "неправильний пароль"));
    }

    public static TheoryData<string, int> TamperedPositions => new()
    {
        { "версія", 4 },
        { "сіль", 5 },
        { "nonce", 5 + KeyDerivation.SaltSize },
        { "шифротекст", EncryptedEnvelope.HeaderSize + 3 },
        { "тег", -1 },
    };

    [Theory]
    [MemberData(nameof(TamperedPositions))]
    public void Decrypt_OneByteChanged_Throws(string part, int position)
    {
        Assert.NotEmpty(part);
        var bytes = AesGcmCipher.Encrypt(Plaintext, Password).ToBytes();
        var index = position >= 0 ? position : bytes.Length - 1;
        bytes[index] ^= 0x01;

        Assert.Throws<BackupDecryptionException>(() => AesGcmCipher.Decrypt(EncryptedEnvelope.Parse(bytes), Password));
    }

    [Fact]
    public void Parse_NotLocalMindFile_Throws()
    {
        var bytes = AesGcmCipher.Encrypt(Plaintext, Password).ToBytes();
        bytes[0] = (byte)'X';

        Assert.Throws<BackupDecryptionException>(() => EncryptedEnvelope.Parse(bytes));
    }

    [Fact]
    public void Parse_Truncated_Throws()
    {
        var bytes = AesGcmCipher.Encrypt(Plaintext, Password).ToBytes();

        Assert.Throws<BackupDecryptionException>(() => EncryptedEnvelope.Parse(bytes.AsMemory(0, EncryptedEnvelope.HeaderSize)));
    }

    [Fact]
    public void Encrypt_SameInput_UsesUniqueNonceAndSalt()
    {
        var first = AesGcmCipher.Encrypt(Plaintext, Password);
        var second = AesGcmCipher.Encrypt(Plaintext, Password);

        Assert.False(first.Nonce.Span.SequenceEqual(second.Nonce.Span));
        Assert.False(first.Salt.Span.SequenceEqual(second.Salt.Span));
        Assert.False(first.Ciphertext.Span.SequenceEqual(second.Ciphertext.Span));
    }

    [Fact]
    public async Task EncryptDecryptAsync_Streams_RoundTrip()
    {
        using var encrypted = new MemoryStream();
        await AesGcmCipher.EncryptAsync(new MemoryStream(Plaintext), encrypted, Password);
        encrypted.Position = 0;
        using var decrypted = new MemoryStream();

        await AesGcmCipher.DecryptAsync(encrypted, decrypted, Password);

        Assert.Equal(Plaintext, decrypted.ToArray());
    }

    [Fact]
    public async Task DecryptAsync_WrongPassword_WritesNothing()
    {
        using var encrypted = new MemoryStream(AesGcmCipher.Encrypt(Plaintext, Password).ToBytes());
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<BackupDecryptionException>(() => AesGcmCipher.DecryptAsync(encrypted, output, "інший"));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Encrypt_EmptyPassword_Throws()
    {
        Assert.Throws<ArgumentException>(() => AesGcmCipher.Encrypt(Plaintext, ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task StreamMethods_EmptyPassword_ThrowArgumentException(string? password)
    {
        using var input = new MemoryStream(Plaintext);
        using var output = new MemoryStream();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => AesGcmCipher.EncryptAsync(input, output, password!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => AesGcmCipher.DecryptAsync(input, output, password!));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Parse_AnyTruncation_ThrowsBackupDecryptionException()
    {
        var bytes = AesGcmCipher.Encrypt([], Password).ToBytes();

        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = bytes.AsMemory(0, length);
            Assert.Throws<BackupDecryptionException>(() => EncryptedEnvelope.Parse(truncated));
        }
    }

    [Fact]
    public async Task EncryptDecryptAsync_LargeNonSeekableStream_RoundTrips()
    {
        var data = new byte[300_000];
        Random.Shared.NextBytes(data);
        using var encrypted = new MemoryStream();
        await AesGcmCipher.EncryptAsync(new NonSeekableStream(data), encrypted, Password);
        using var decrypted = new MemoryStream();

        await AesGcmCipher.DecryptAsync(new NonSeekableStream(encrypted.ToArray()), decrypted, Password);

        Assert.Equal(data, decrypted.ToArray());
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
