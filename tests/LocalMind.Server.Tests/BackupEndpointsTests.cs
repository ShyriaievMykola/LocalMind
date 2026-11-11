using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using LocalMind.Core.Backup.Crypto;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Server.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Server.Tests;

public sealed class BackupEndpointsTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    private const string Password = "довгий пароль для тесту";
    private static readonly Uri Backups = new("/api/v1/backups", UriKind.Relative);

    [Fact]
    public async Task FullCycle_UploadListDownloadDelete()
    {
        using var client = await AuthorizedClientAsync(factory);
        var encrypted = AesGcmCipher.Encrypt("нотатки"u8, "пароль шифрування").ToBytes();

        var info = await UploadOkAsync(client, encrypted);
        var list = await client.GetFromJsonAsync<BackupInfo[]>(Backups);
        var downloaded = await client.GetByteArrayAsync(new Uri($"/api/v1/backups/{info.Id}", UriKind.Relative));
        using var delete = await client.DeleteAsync(new Uri($"/api/v1/backups/{info.Id}", UriKind.Relative));
        using var afterDelete = await client.GetAsync(new Uri($"/api/v1/backups/{info.Id}", UriKind.Relative));

        Assert.Equal(encrypted.Length, info.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(encrypted)), info.Sha256);
        Assert.Equal([info], list!);
        Assert.Equal(encrypted, downloaded);
        Assert.Equal("нотатки"u8.ToArray(), AesGcmCipher.Decrypt(EncryptedEnvelope.Parse(downloaded), "пароль шифрування"));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<BackupInfo[]>(Backups) ?? []);
    }

    [Fact]
    public async Task List_NewestFirst()
    {
        using var client = await AuthorizedClientAsync(factory);
        var first = await UploadOkAsync(client, [1]);
        var second = await UploadOkAsync(client, [2]);

        var list = await client.GetFromJsonAsync<BackupInfo[]>(Backups);

        Assert.Equal([second.Id, first.Id], list!.Select(b => b.Id));
    }

    [Fact]
    public async Task OtherUsersBackup_Returns404()
    {
        using var owner = await AuthorizedClientAsync(factory);
        using var stranger = await AuthorizedClientAsync(factory);
        var info = await UploadOkAsync(owner, [1, 2, 3]);
        var url = new Uri($"/api/v1/backups/{info.Id}", UriKind.Relative);

        using var download = await stranger.GetAsync(url);
        using var delete = await stranger.DeleteAsync(url);
        var strangerList = await stranger.GetFromJsonAsync<BackupInfo[]>(Backups);
        using var ownerDownload = await owner.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Empty(strangerList!);
        Assert.Equal(HttpStatusCode.OK, ownerDownload.StatusCode);
    }

    [Fact]
    public async Task UnknownBackup_Returns404()
    {
        using var client = await AuthorizedClientAsync(factory);

        using var response = await client.GetAsync(new Uri($"/api/v1/backups/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        using var list = await client.GetAsync(Backups);
        using var upload = await client.PostAsync(Backups, new ByteArrayContent([1]));

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Upload_TooLarge_Returns413AndStoresNothing(bool sendContentLength)
    {
        using var small = factory.WithWebHostBuilder(b => b.UseSetting("Storage:MaxBackupBytes", "1024"));
        using var client = await AuthorizedClientAsync(small);
        var data = new byte[1025];
        HttpContent content = sendContentLength ? new ByteArrayContent(data) : new StreamContent(new UnknownLengthStream(data));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var filesBefore = Directory.GetFiles(factory.StorageRoot).Length;

        using var response = await client.PostAsync(Backups, content);
        content.Dispose();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<BackupInfo[]>(Backups) ?? []);
        Assert.Equal(filesBefore, Directory.GetFiles(factory.StorageRoot).Length);
    }

    [Fact]
    public async Task Upload_ExactlyAtLimit_Succeeds()
    {
        using var small = factory.WithWebHostBuilder(b => b.UseSetting("Storage:MaxBackupBytes", "1024"));
        using var client = await AuthorizedClientAsync(small);

        var info = await UploadOkAsync(client, new byte[1024]);

        Assert.Equal(1024, info.SizeBytes);
    }

    [Fact]
    public async Task Upload_OverCountQuota_Returns409UntilOldDeleted()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Storage:MaxBackupsPerUser", "2"));
        using var client = await AuthorizedClientAsync(app);
        var first = await UploadOkAsync(client, [1]);
        await UploadOkAsync(client, [2]);

        using var third = await PostAsync(client, [3]);
        using var delete = await client.DeleteAsync(new Uri($"/api/v1/backups/{first.Id}", UriKind.Relative));
        using var afterDelete = await PostAsync(client, [3]);

        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal(HttpStatusCode.Created, afterDelete.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Upload_OverTotalBytesQuota_Returns409AndStoresNothing(bool sendContentLength)
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Storage:MaxTotalBytesPerUser", "100"));
        using var client = await AuthorizedClientAsync(app);
        await UploadOkAsync(client, new byte[60]);
        var filesBefore = Directory.GetFiles(factory.StorageRoot).Length;

        HttpContent content = sendContentLength
            ? new ByteArrayContent(new byte[41])
            : new StreamContent(new UnknownLengthStream(new byte[41]));
        using var response = await client.PostAsync(Backups, content);
        content.Dispose();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await client.GetFromJsonAsync<BackupInfo[]>(Backups) ?? []);
        Assert.Equal(filesBefore, Directory.GetFiles(factory.StorageRoot).Length);
    }

    [Fact]
    public async Task Upload_ParallelRequests_CannotExceedCountQuota()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Storage:MaxBackupsPerUser", "2"));
        using var client = await AuthorizedClientAsync(app);
        var filesBefore = Directory.GetFiles(factory.StorageRoot).Length;

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => PostAsync(client, [(byte)i])));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2, (await client.GetFromJsonAsync<BackupInfo[]>(Backups))!.Length);
        Assert.Equal(filesBefore + 2, Directory.GetFiles(factory.StorageRoot).Length);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Upload_ParallelRequests_CannotExceedBytesQuota()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Storage:MaxTotalBytesPerUser", "100"));
        using var client = await AuthorizedClientAsync(app);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostAsync(client, new byte[40])));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(80, (await client.GetFromJsonAsync<BackupInfo[]>(Backups))!.Sum(b => b.SizeBytes));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Quota_IsPerUser()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Storage:MaxBackupsPerUser", "1"));
        using var first = await AuthorizedClientAsync(app);
        using var second = await AuthorizedClientAsync(app);
        await UploadOkAsync(first, [1]);

        var info = await UploadOkAsync(second, [2]);

        Assert.Equal(1, info.SizeBytes);
    }

    [Fact]
    public async Task RootPathWithTrailingSeparator_Works()
    {
        var root = Path.Combine(factory.StorageRoot, "trailing") + Path.DirectorySeparatorChar;
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Storage:RootPath", root));
        using var client = await AuthorizedClientAsync(app);

        var info = await UploadOkAsync(client, [1, 2, 3]);
        var downloaded = await client.GetByteArrayAsync(new Uri($"/api/v1/backups/{info.Id}", UriKind.Relative));

        Assert.Equal([1, 2, 3], downloaded);
    }

    [Fact]
    public async Task Upload_DeletedUserWithValidToken_Returns401()
    {
        using var client = await AuthorizedClientAsync(factory);
        var me = await client.GetFromJsonAsync<UserInfo>(new Uri("/api/v1/auth/me", UriKind.Relative));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            await users.DeleteAsync((await users.FindByIdAsync(me!.Id))!);
        }

        using var content = new ByteArrayContent([1]);
        using var response = await client.PostAsync(Backups, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WhileDownloadStreamOpen_Succeeds()
    {
        using var client = await AuthorizedClientAsync(factory);
        var info = await UploadOkAsync(client, new byte[10]);
        var store = factory.Services.GetRequiredService<FileSystemBlobStore>();

        await using (var reading = store.OpenRead(info.Id))
        {
            Assert.NotNull(reading);
            using var delete = await client.DeleteAsync(new Uri($"/api/v1/backups/{info.Id}", UriKind.Relative));
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.Null(store.OpenRead(info.Id));
    }

    [Theory]
    [InlineData("../escape.lmbk")]
    [InlineData("..\\escape.lmbk")]
    [InlineData("sub/inner.lmbk")]
    [InlineData("/etc/passwd")]
    public void ResolvePath_Traversal_Throws(string fileName)
    {
        var store = factory.Services.GetRequiredService<FileSystemBlobStore>();

        if (fileName.Contains('\\', StringComparison.Ordinal) && !OperatingSystem.IsWindows())
        {
            // На Linux/macOS «\» — звичайний символ імені, файл лишається в теці.
            Assert.Equal(Path.GetFullPath(factory.StorageRoot), Path.GetDirectoryName(store.ResolvePath(fileName)));
            return;
        }

        Assert.Throws<ArgumentException>(() => store.ResolvePath(fileName));
    }

    [Fact]
    public void ResolvePath_PlainName_StaysInRoot()
    {
        var store = factory.Services.GetRequiredService<FileSystemBlobStore>();

        var path = store.ResolvePath("abc.lmbk");

        Assert.Equal(Path.Combine(Path.GetFullPath(factory.StorageRoot), "abc.lmbk"), path);
    }

    private static async Task<HttpClient> AuthorizedClientAsync<T>(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<T> app)
        where T : class
    {
        var client = app.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static async Task<BackupInfo> UploadOkAsync(HttpClient client, byte[] data)
    {
        using var response = await PostAsync(client, data);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BackupInfo>())!;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, byte[] data)
    {
        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await client.PostAsync(Backups, content);
    }

    /// <summary>Потік без відомої довжини — запит іде без Content-Length.</summary>
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
