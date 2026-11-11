namespace LocalMind.Server.Auth;

/// <summary>Ролі сервера (SV-08). Кожен зареєстрований користувач — <see cref="User"/>; адміністратора задає лише конфігурація.</summary>
public static class AppRoles
{
    public const string User = "User";

    public const string Admin = "Admin";
}
