using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Core.Errors;

namespace LocalMind.Core.Backup;

/// <summary>
/// Typed HttpClient сервера бекапу (B-05). Підставляє access-токен, а коли той спливає або сервер відповідає 401 —
/// один раз оновлює пару через refresh-токен і повторює запит. Якщо оновлення не вдалось (сесію відкликано,
/// зокрема через повторне використання токена), токени стираються і кидається <see cref="BackupApiException"/>
/// з <see cref="BackupApiException.IsSessionExpired"/>.
/// </summary>
public sealed class BackupApiClient(HttpClient http, ITokenStore tokens, TimeProvider time)
{
    private const string OctetStream = "application/octet-stream";

    /// <summary>За скільки до кінця строку access-токен оновлюємо заздалегідь.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);

    // Статичний: typed HttpClient створюється заново для кожного сервісу, а сесія в процесі одна.
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    /// <summary>Чи є збережена сесія.</summary>
    public bool IsSignedIn => tokens.Load() is not null;

    /// <summary>Реєстрація облікового запису (без входу).</summary>
    /// <exception cref="BackupApiException">Email зайнятий, слабкий пароль, сервер недоступний.</exception>
    public async Task RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/register")
            {
                Content = JsonContent.Create(new RegisterRequest(email, password)),
            },
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Вхід; токени зберігаються в <see cref="ITokenStore"/>.</summary>
    /// <exception cref="BackupApiException">Неправильний email або пароль, сервер недоступний.</exception>
    public async Task LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest(email, password)),
            },
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        tokens.Save(await ReadAsync<TokenResponse>(response, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Вихід: відкликає сесію на сервері (якщо він доступний) і стирає токени локально.</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var current = tokens.Load();
        tokens.Clear();
        if (current is null)
        {
            return;
        }

        try
        {
            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/logout")
                {
                    Content = JsonContent.Create(new LogoutRequest(current.RefreshToken)),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (BackupApiException)
        {
            // Сервер недоступний — локально сесії вже немає, на сервері вона спливе сама.
        }
    }

    /// <summary>Поточний користувач.</summary>
    public async Task<UserInfo> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "api/v1/auth/me"), cancellationToken).ConfigureAwait(false);
        return await ReadAsync<UserInfo>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Завантажує зашифрований бекап.</summary>
    /// <exception cref="BackupApiException">Завеликий бекап (413), сесія завершилась, сервер недоступний.</exception>
    public async Task<BackupInfo> UploadBackupAsync(ReadOnlyMemory<byte> encrypted, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () =>
            {
                var content = new ReadOnlyMemoryContent(encrypted);
                content.Headers.ContentType = new MediaTypeHeaderValue(OctetStream);
                return new HttpRequestMessage(HttpMethod.Post, "api/v1/backups") { Content = content };
            },
            cancellationToken).ConfigureAwait(false);
        return await ReadAsync<BackupInfo>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Бекапи користувача, новіші першими.</summary>
    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "api/v1/backups"), cancellationToken).ConfigureAwait(false);
        return await ReadAsync<BackupInfo[]>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Скачує зашифрований бекап.</summary>
    /// <exception cref="BackupApiException">Бекапу немає (404), сесія завершилась, сервер недоступний.</exception>
    public async Task<byte[]> DownloadBackupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"api/v1/backups/{id}"), cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Видаляє бекап.</summary>
    public async Task DeleteBackupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, $"api/v1/backups/{id}"), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Запит з access-токеном. Запит створюється фабрикою, бо при повторі після оновлення токена
    /// потрібен новий <see cref="HttpRequestMessage"/>. Неуспішна відповідь → <see cref="BackupApiException"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        var current = tokens.Load() ?? throw SessionExpired();
        if (current.AccessTokenExpiresAt - time.GetUtcNow() < RefreshMargin)
        {
            current = await RefreshAsync(current, cancellationToken).ConfigureAwait(false);
        }

        var response = await SendWithTokenAsync(createRequest, current, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            current = await RefreshAsync(current, cancellationToken).ConfigureAwait(false);
            response = await SendWithTokenAsync(createRequest, current, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                tokens.Clear();
                throw SessionExpired();
            }
        }

        try
        {
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private Task<HttpResponseMessage> SendWithTokenAsync(
        Func<HttpRequestMessage> createRequest, TokenResponse current, CancellationToken cancellationToken) =>
        SendAsync(
            () =>
            {
                var request = createRequest();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.AccessToken);
                return request;
            },
            cancellationToken);

    /// <summary>
    /// Оновлює пару токенів. Паралельні запити, що одночасно отримали 401, оновлюють лише раз:
    /// інакше другий пред'явив би вже використаний refresh-токен, і сервер відкликав би сесію.
    /// </summary>
    private async Task<TokenResponse> RefreshAsync(TokenResponse stale, CancellationToken cancellationToken)
    {
        await RefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = tokens.Load() ?? throw SessionExpired();
            if (current.RefreshToken != stale.RefreshToken)
            {
                return current;
            }

            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/refresh")
                {
                    Content = JsonContent.Create(new RefreshRequest(current.RefreshToken)),
                },
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                tokens.Clear();
                throw SessionExpired();
            }

            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var refreshed = await ReadAsync<TokenResponse>(response, cancellationToken).ConfigureAwait(false);
            tokens.Save(refreshed);
            return refreshed;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        using var request = createRequest();
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new BackupApiException(null, "Сервер бекапу недоступний. Перевірте підключення й адресу сервера.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BackupApiException(null, "Сервер бекапу не відповідає.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var problem = await TryReadProblemAsync(response, cancellationToken).ConfigureAwait(false);
        var details = problem?.Errors?.SelectMany(e => e.Value).ToArray() ?? [];
        var message = response.StatusCode switch
        {
            HttpStatusCode.RequestEntityTooLarge => problem?.Title ?? "Бекап завеликий для сервера.",
            HttpStatusCode.NotFound => "Бекап не знайдено.",
            _ when details.Length > 0 => string.Join(" ", details),
            _ => problem?.Title ?? $"Сервер бекапу повернув помилку {(int)response.StatusCode}.",
        };
        throw new BackupApiException(response.StatusCode, message);
    }

    private static async Task<Problem?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Problem>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
        ?? throw new BackupApiException(response.StatusCode, "Сервер бекапу повернув порожню відповідь.");

    private static BackupApiException SessionExpired() =>
        new(HttpStatusCode.Unauthorized, "Сесія завершилась. Увійдіть знову.");

    /// <summary>Поля ProblemDetails, які показуємо користувачу.</summary>
    private sealed record Problem(string? Title, Dictionary<string, string[]>? Errors);
}
