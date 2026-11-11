using LocalMind.Server.Audit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LocalMind.Server.Auth;

/// <summary>
/// Початковий адміністратор з конфігурації (секція <c>Admin</c>, DR-034). Пароль береться зі змінної середовища
/// <c>Admin__Password</c>; після першого запуску його можна прибрати.
/// </summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "Admin";

    public string? Email { get; set; }

    public string? Password { get; set; }

    internal bool IsConfigured => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrEmpty(Password);

    /// <summary>Або обидва поля, або жодного: половина налаштування — майже напевно помилка.</summary>
    internal bool IsConsistent => string.IsNullOrWhiteSpace(Email) == string.IsNullOrEmpty(Password);
}

/// <summary>Створює ролі й початкового адміністратора на старті сервера.</summary>
public static partial class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var name in new[] { AppRoles.User, AppRoles.Admin })
        {
            if (!await roles.RoleExistsAsync(name))
            {
                Throw(await roles.CreateAsync(new IdentityRole(name)), $"створення ролі {name}");
            }
        }

        var options = services.GetRequiredService<IOptions<AdminSeedOptions>>().Value;
        if (!options.IsConfigured)
        {
            return;
        }

        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));
        var email = EmailFormatValidator.Normalize(options.Email!);
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Існуючий акаунт не підвищуємо: інакше той, хто першим зареєстрував цей email, став би адміністратором.
            if (!await users.IsInRoleAsync(existing, AppRoles.Admin))
            {
                LogNotPromoted(logger);
            }

            return;
        }

        var admin = new IdentityUser { UserName = email, Email = email };
        Throw(await users.CreateAsync(admin, options.Password!), "створення адміністратора (перевірте Admin:Email і Admin:Password)");
        Throw(await users.AddToRolesAsync(admin, [AppRoles.User, AppRoles.Admin]), "призначення ролей адміністратору");
        var audit = services.GetRequiredService<AuditLog>();
        await audit.WriteAsync(AuditActions.AdminSeeded, admin.Id);
    }

    // Опис помилки містить лише коди й тексти Identity, без пароля.
    private static void Throw(IdentityResult result, string step)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Не вдалося виконати «{step}»: {string.Join(" ", result.Errors.Select(e => e.Description))}");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Admin:Email уже зареєстровано як звичайний обліковий запис — його не призначено адміністратором")]
    private static partial void LogNotPromoted(ILogger logger);
}
