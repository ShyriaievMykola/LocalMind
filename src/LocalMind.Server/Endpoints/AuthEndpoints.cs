using System.Security.Claims;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Server.Audit;
using LocalMind.Server.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace LocalMind.Server.Endpoints;

/// <summary>Реєстрація, вхід (SV-03), оновлення токенів і вихід (SV-05).</summary>
public static class AuthEndpoints
{
    internal const string InvalidCredentials = "Неправильний email або пароль.";
    internal const string InvalidRefreshToken = "Сесія недійсна або завершилась. Увійдіть знову.";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var auth = api.MapGroup("/auth").WithTags("Auth");

        // Реєстрація й вхід — під суворим лімітом (перебір паролів і email), решта — під м'якшим.
        auth.MapPost("/register", RegisterAsync).RequireRateLimiting(AuthRateLimitOptions.PolicyName);
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(AuthRateLimitOptions.PolicyName);
        auth.MapPost("/refresh", RefreshAsync).RequireRateLimiting(AuthRateLimitOptions.SessionPolicyName);
        auth.MapPost("/logout", LogoutAsync).RequireRateLimiting(AuthRateLimitOptions.SessionPolicyName);
        auth.MapGet("/me", MeAsync).RequireAuthorization().RequireRateLimiting(AuthRateLimitOptions.SessionPolicyName);
        return api;
    }

    private static async Task<Results<Created, ValidationProblem>> RegisterAsync(
        RegisterRequest request, UserManager<IdentityUser> users, AuditLog audit)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Email і пароль обов'язкові."],
            });
        }

        var email = EmailFormatValidator.Normalize(request.Email);
        var user = new IdentityUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, request.Password);
        if (result.Succeeded)
        {
            await users.AddToRoleAsync(user, AppRoles.User);
            await audit.WriteAsync(AuditActions.Registered, user.Id);
        }

        // Зайнятий email дає ту саму відповідь, що й успішна реєстрація: інакше реєстрація дозволяла б
        // перевіряти, чи є в сервері такий користувач (DR-032). Пароль Identity перевіряє раніше, ніж унікальність.
        if (result.Succeeded || result.Errors.All(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
            or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return TypedResults.Created();
        }

        return TypedResults.ValidationProblem(result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "email")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn,
        IPasswordHasher<IdentityUser> hasher, JwtTokenService tokens, RefreshTokenService refresh, AuditLog audit,
        CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(EmailFormatValidator.Normalize(request.Email));
        if (user is null)
        {
            // Хешуємо й для неіснуючого користувача, щоб час відповіді не видавав, чи є такий email.
            hasher.VerifyHashedPassword(DummyUser, DummyHash.Value, request.Password ?? string.Empty);
            await audit.WriteAsync(AuditActions.LoginFailed, detail: "unknown_user");
            return Unauthorized();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password ?? string.Empty, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            // Для заблокованого акаунта Identity не хешує пароль — хешуємо самі, щоб і час, і тіло відповіді
            // були такими самими, як для неправильного пароля чи неіснуючого email.
            hasher.VerifyHashedPassword(DummyUser, DummyHash.Value, request.Password ?? string.Empty);
        }

        if (!result.Succeeded)
        {
            await audit.WriteAsync(AuditActions.LoginFailed, user.Id, detail: result.IsLockedOut ? "locked_out" : "wrong_password");
            return Unauthorized();
        }

        var (refreshToken, refreshExpiresAt) = await refresh.IssueAsync(user, cancellationToken);
        await audit.WriteAsync(AuditActions.LoginSucceeded, user.Id);
        return TypedResults.Ok(await CreateResponseAsync(tokens, users, user, refreshToken, refreshExpiresAt));
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RefreshAsync(
        RefreshRequest request, RefreshTokenService refresh, JwtTokenService tokens, UserManager<IdentityUser> users,
        CancellationToken cancellationToken)
    {
        var rotated = await refresh.RotateAsync(request.RefreshToken ?? string.Empty, cancellationToken);
        if (rotated is not { } r)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidRefreshToken);
        }

        return TypedResults.Ok(await CreateResponseAsync(tokens, users, r.User, r.Token, r.ExpiresAt));
    }

    private static async Task<NoContent> LogoutAsync(
        LogoutRequest request, RefreshTokenService refresh, CancellationToken cancellationToken)
    {
        // Завжди 204: відповідь не підказує, чи був токен чинним.
        await refresh.RevokeAsync(request.RefreshToken ?? string.Empty, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<TokenResponse> CreateResponseAsync(
        JwtTokenService tokens, UserManager<IdentityUser> users, IdentityUser user, string refreshToken,
        DateTimeOffset refreshExpiresAt)
    {
        var (accessToken, expiresAt) = tokens.CreateAccessToken(user, await users.GetRolesAsync(user));
        return new TokenResponse(accessToken, expiresAt, refreshToken, refreshExpiresAt);
    }

    private static async Task<Results<Ok<UserInfo>, ChallengeHttpResult>> MeAsync(
        ClaimsPrincipal principal, UserManager<IdentityUser> users)
    {
        var user = principal.Identity?.Name is { } id ? await users.FindByIdAsync(id) : null;
        return user is null
            ? TypedResults.Challenge()
            : TypedResults.Ok(new UserInfo(user.Id, user.Email ?? string.Empty));
    }

    private static ProblemHttpResult Unauthorized() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCredentials);

    private static readonly IdentityUser DummyUser = new();

    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<IdentityUser>().HashPassword(DummyUser, Guid.NewGuid().ToString()));
}
