using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Server.Audit;
using LocalMind.Server.Auth;
using LocalMind.Server.Data;
using LocalMind.Server.Data.Entities;
using LocalMind.Server.Endpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LocalMind.Server.Tests;

/// <summary>Ролі User/Admin й аудит подій (SV-08, SV-11, DR-034).</summary>
public sealed class AdminAndAuditTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    private const string AdminEmail = "admin-owner@example.com";
    private const string AdminPassword = "пароль адміністратора 1";
    private const string Password = "довгий пароль для тесту";

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> AdminApp() =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Admin:Email", AdminEmail);
            b.UseSetting("Admin:Password", AdminPassword);
        });

    [Fact]
    public async Task AdminEndpoints_WithoutToken_Returns401()
    {
        using var app = AdminApp();
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/admin/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_AsUser_Return403()
    {
        using var app = AdminApp();
        using var user = await SignedInClientAsync(app, NewEmail(), Password);
        var someId = Guid.NewGuid().ToString();

        using var list = await user.GetAsync(new Uri("/api/v1/admin/users", UriKind.Relative));
        using var audit = await user.GetAsync(new Uri("/api/v1/admin/audit", UriKind.Relative));
        using var block = await user.PostAsync(new Uri($"/api/v1/admin/users/{someId}/block", UriKind.Relative), null);
        using var unblock = await user.PostAsync(new Uri($"/api/v1/admin/users/{someId}/unblock", UriKind.Relative), null);

        Assert.All([list, audit, block, unblock], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    [Fact]
    public async Task AdminEndpoints_AsAdmin_ListUsersWithoutSecrets()
    {
        using var app = AdminApp();
        var email = NewEmail();
        using var user = await SignedInClientAsync(app, email, Password);
        using var upload = await user.PostAsync(new Uri("/api/v1/backups", UriKind.Relative), new ByteArrayContent(new byte[7]));
        using var admin = await SignedInClientAsync(app, AdminEmail, AdminPassword);

        using var response = await admin.GetAsync(new Uri("/api/v1/admin/users?take=500", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        var users = JsonSerializer.Deserialize<AdminUserInfo[]>(body, JsonSerializerOptions.Web)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single(users, u => u.Email == email);
        Assert.False(listed.IsAdmin);
        Assert.False(listed.IsBlocked);
        Assert.Equal(1, listed.BackupCount);
        Assert.Equal(7, listed.BackupBytes);
        Assert.True(Assert.Single(users, u => u.Email == AdminEmail).IsAdmin);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tokens_CarryRoles()
    {
        using var app = AdminApp();
        using var userClient = app.CreateClient();
        var userTokens = await RegisterAndLoginAsync(userClient, NewEmail(), Password);
        var adminTokens = await RegisterAndLoginAsync(userClient, AdminEmail, AdminPassword, register: false);

        Assert.Equal([AppRoles.User], RolesOf(userTokens));
        Assert.Equivalent(new[] { AppRoles.User, AppRoles.Admin }, RolesOf(adminTokens));
    }

    [Fact]
    public async Task Block_PreventsLoginAndRefresh_UnblockRestores()
    {
        using var app = AdminApp();
        var email = NewEmail();
        using var plain = app.CreateClient();
        var tokens = await RegisterAndLoginAsync(plain, email, Password);
        using var admin = await SignedInClientAsync(app, AdminEmail, AdminPassword);
        var id = await IdOfAsync(admin, email);

        using var block = await admin.PostAsync(new Uri($"/api/v1/admin/users/{id}/block", UriKind.Relative), null);
        using var login = await plain.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        using var refresh = await plain.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken));
        using var unblock = await admin.PostAsync(new Uri($"/api/v1/admin/users/{id}/unblock", UriKind.Relative), null);
        using var loginAgain = await plain.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));

        Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unblock.StatusCode);
        Assert.True(loginAgain.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Block_Self_Returns409_UnknownUser_Returns404()
    {
        using var app = AdminApp();
        using var admin = await SignedInClientAsync(app, AdminEmail, AdminPassword);
        var adminId = await IdOfAsync(admin, AdminEmail);

        using var self = await admin.PostAsync(new Uri($"/api/v1/admin/users/{adminId}/block", UriKind.Relative), null);
        using var unknown = await admin.PostAsync(new Uri($"/api/v1/admin/users/{Guid.NewGuid()}/block", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task AdminToken_AfterRoleRemoved_IsForbiddenImmediately()
    {
        using var app = AdminApp();
        var email = NewEmail();
        using var user = await SignedInClientAsync(app, email, Password);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            await users.AddToRoleAsync((await users.FindByEmailAsync(email))!, AppRoles.Admin);
        }

        // Токен видано до підвищення — ролі Admin в ньому немає; тож заходимо знову.
        using var promoted = await SignedInClientAsync(app, email, Password);
        using var before = await promoted.GetAsync(new Uri("/api/v1/admin/users", UriKind.Relative));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            await users.RemoveFromRoleAsync((await users.FindByEmailAsync(email))!, AppRoles.Admin);
        }

        using var after = await promoted.GetAsync(new Uri("/api/v1/admin/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact]
    public async Task Seed_ExistingRegularAccount_IsNotPromoted()
    {
        var victim = NewEmail();
        using (var plain = factory.CreateClient())
        {
            using var register = await plain.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(victim, Password));
        }

        using var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Admin:Email", victim);
            b.UseSetting("Admin:Password", AdminPassword);
        });
        using var client = await SignedInClientAsync(app, victim, Password);

        using var response = await client.GetAsync(new Uri("/api/v1/admin/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("only-email@example.com", null)]
    [InlineData(null, "пароль без email 123")]
    [InlineData("weak-admin@example.com", "коротко")]
    public void Seed_IncompleteOrWeakSettings_StopStartup(string? email, string? password)
    {
        using var app = factory.WithWebHostBuilder(b =>
        {
            if (email is not null)
            {
                b.UseSetting("Admin:Email", email);
            }

            if (password is not null)
            {
                b.UseSetting("Admin:Password", password);
            }
        });

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }

    [Fact]
    public async Task Audit_RecordsLoginRefreshLogoutAndBackupDelete_WithoutSecrets()
    {
        using var app = AdminApp();
        using var client = app.CreateClient();
        var email = NewEmail();
        var ghost = $"ghost-{Guid.NewGuid():N}@example.com";
        var typedPassword = $"введено-замість-email-{Guid.NewGuid():N}";
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));
        using var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "неправильний пароль"));
        using var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(ghost, Password));
        using var unknownPasswordAsEmail = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(typedPassword, Password));
        var tokens = await LoginAsync(client, email, Password);
        var refreshed = await RefreshOkAsync(client, tokens.RefreshToken);
        using var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken));
        using var authorized = Authorized(app, refreshed.AccessToken);
        using var upload = await authorized.PostAsync(new Uri("/api/v1/backups", UriKind.Relative), new ByteArrayContent([1, 2]));
        var info = (await upload.Content.ReadFromJsonAsync<BackupInfo>())!;
        using var delete = await authorized.DeleteAsync(new Uri($"/api/v1/backups/{info.Id}", UriKind.Relative));
        var again = await LoginAsync(client, email, Password);
        using var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(again.RefreshToken));

        var entries = await ReadAuditAsync(app);
        var user = (await ReadUserAsync(app, email)).Id;
        var mine = entries.Where(e => e.UserId == user).Select(e => e.Action).ToList();

        Assert.Contains(AuditActions.Registered, mine);
        Assert.Contains(AuditActions.LoginFailed, mine);
        Assert.Contains(AuditActions.LoginSucceeded, mine);
        Assert.Contains(AuditActions.RefreshSucceeded, mine);
        Assert.Contains(AuditActions.RefreshReuseDetected, mine);
        Assert.Contains(AuditActions.BackupUploaded, mine);
        Assert.Contains(AuditActions.BackupDeleted, mine);
        Assert.Contains(AuditActions.Logout, mine);
        Assert.Contains(entries, e => e is { Action: AuditActions.LoginFailed, UserId: null, Detail: "unknown_user" });

        // У записах не повинно бути ні паролів, ні токенів, ні введеного в поле email тексту.
        var dump = JsonSerializer.Serialize(entries);
        foreach (var secret in new[] { Password, "неправильний пароль", tokens.AccessToken, tokens.RefreshToken,
            refreshed.AccessToken, refreshed.RefreshToken, again.RefreshToken, ghost, typedPassword, email })
        {
            Assert.DoesNotContain(secret, dump, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Audit_AdminCanReadEntriesAndFilterByUser()
    {
        using var app = AdminApp();
        var email = NewEmail();
        using var user = await SignedInClientAsync(app, email, Password);
        using var admin = await SignedInClientAsync(app, AdminEmail, AdminPassword);
        var id = await IdOfAsync(admin, email);

        var entries = await admin.GetFromJsonAsync<AuditRecord[]>($"/api/v1/admin/audit?userId={id}&take=50");

        Assert.NotEmpty(entries!);
        Assert.All(entries!, e => Assert.Equal(id, e.UserId));
        Assert.Contains(entries!, e => e.Action == AuditActions.LoginSucceeded);
    }

    [Fact]
    public async Task Audit_BlockAndUnblock_RecordAdminAsActor()
    {
        using var app = AdminApp();
        var email = NewEmail();
        using var plain = app.CreateClient();
        using var register = await plain.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));
        using var admin = await SignedInClientAsync(app, AdminEmail, AdminPassword);
        var id = await IdOfAsync(admin, email);
        var adminId = await IdOfAsync(admin, AdminEmail);

        using var block = await admin.PostAsync(new Uri($"/api/v1/admin/users/{id}/block", UriKind.Relative), null);
        using var unblock = await admin.PostAsync(new Uri($"/api/v1/admin/users/{id}/unblock", UriKind.Relative), null);

        var entries = await ReadAuditAsync(app);
        Assert.Contains(entries, e => e.Action == AuditActions.UserBlocked && e.UserId == id && e.ActorId == adminId);
        Assert.Contains(entries, e => e.Action == AuditActions.UserUnblocked && e.UserId == id && e.ActorId == adminId);
    }

    [Fact]
    public async Task AuditCleanup_DeletesOnlyEntriesOlderThanRetention()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Audit:RetentionDays", "30"));
        _ = app.CreateClient();
        var marker = Guid.NewGuid().ToString("N");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            db.AuditEntries.Add(new AuditEntryEntity { Action = "test.old", Detail = marker, OccurredAt = DateTimeOffset.UtcNow.AddDays(-31) });
            db.AuditEntries.Add(new AuditEntryEntity { Action = "test.recent", Detail = marker, OccurredAt = DateTimeOffset.UtcNow.AddDays(-29) });
            await db.SaveChangesAsync();
        }

        var deleted = await app.Services.GetRequiredService<RefreshTokenCleanupService>().CleanupAuditAsync();

        Assert.True(deleted >= 1);
        var left = (await ReadAuditAsync(app)).Where(e => e.Detail == marker).Select(e => e.Action).ToList();
        Assert.Equal(["test.recent"], left);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static async Task<List<AuditEntryEntity>> ReadAuditAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ServerDbContext>().AuditEntries.AsNoTracking().ToListAsync();
    }

    private static async Task<IdentityUser> ReadUserAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().FindByEmailAsync(email))!;
    }

    private static async Task<string> IdOfAsync(HttpClient admin, string email)
    {
        var users = await admin.GetFromJsonAsync<AdminUserInfo[]>("/api/v1/admin/users?take=500");
        return users!.Single(u => u.Email == email).Id;
    }

    private static string[] RolesOf(TokenResponse tokens) =>
        new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken).Claims
            .Where(c => c.Type == JwtTokenService.RoleClaimType).Select(c => c.Value).ToArray();

    private static async Task<HttpClient> SignedInClientAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string email, string password)
    {
        var client = app.CreateClient();
        var tokens = await RegisterAndLoginAsync(client, email, password, register: email != AdminEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static HttpClient Authorized(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, string accessToken)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task<TokenResponse> RegisterAndLoginAsync(HttpClient client, string email, string password, bool register = true)
    {
        if (register)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password));
        }

        return await LoginAsync(client, email, password);
    }

    private static async Task<TokenResponse> LoginAsync(HttpClient client, string email, string password)
    {
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static async Task<TokenResponse> RefreshOkAsync(HttpClient client, string token)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(token));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }
}
