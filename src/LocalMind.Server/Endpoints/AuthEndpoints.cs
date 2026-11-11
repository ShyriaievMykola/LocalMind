using System.Security.Claims;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Server.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace LocalMind.Server.Endpoints;

/// <summary>Реєстрація і вхід (SV-03).</summary>
public static class AuthEndpoints
{
    internal const string InvalidCredentials = "Неправильний email або пароль.";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var auth = api.MapGroup("/auth").WithTags("Auth");
        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/login", LoginAsync);
        auth.MapGet("/me", MeAsync).RequireAuthorization();
        return api;
    }

    private static async Task<Results<Created, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        RegisterRequest request, UserManager<IdentityUser> users)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Email і пароль обов'язкові."],
            });
        }

        var email = request.Email.Trim();
        var user = new IdentityUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, request.Password);
        if (result.Succeeded)
        {
            return TypedResults.Created();
        }

        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
            or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Обліковий запис із таким email уже існує.");
        }

        return TypedResults.ValidationProblem(result.Errors
            .GroupBy(e => e.Code.Contains("Password", StringComparison.Ordinal) ? "password" : "email")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn,
        IPasswordHasher<IdentityUser> hasher, JwtTokenService tokens)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Хешуємо й для неіснуючого користувача, щоб час відповіді не видавав, чи є такий email.
            hasher.VerifyHashedPassword(DummyUser, DummyHash.Value, request.Password ?? string.Empty);
            return Unauthorized();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password ?? string.Empty, lockoutOnFailure: true);

        // Заблокований обліковий запис дає ту саму відповідь — інакше блокування підтверджує, що email існує.
        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        var (accessToken, expiresAt) = tokens.CreateAccessToken(user);

        // Refresh-токен з'явиться в SV-05 (#79).
        return TypedResults.Ok(new TokenResponse(accessToken, expiresAt, string.Empty, expiresAt));
    }

    private static async Task<Results<Ok<UserInfo>, UnauthorizedHttpResult>> MeAsync(
        ClaimsPrincipal principal, UserManager<IdentityUser> users)
    {
        var user = principal.Identity?.Name is { } id ? await users.FindByIdAsync(id) : null;
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new UserInfo(user.Id, user.Email ?? string.Empty));
    }

    private static ProblemHttpResult Unauthorized() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCredentials);

    private static readonly IdentityUser DummyUser = new();

    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<IdentityUser>().HashPassword(DummyUser, Guid.NewGuid().ToString()));
}
