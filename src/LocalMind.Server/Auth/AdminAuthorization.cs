using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace LocalMind.Server.Auth;

/// <summary>Політика адмін-ендпоінтів: роль Admin у токені й актуальний стан у БД.</summary>
public static class AdminAuthorization
{
    public const string PolicyName = "Admin";

    public static IServiceCollection AddAdminAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder().AddPolicy(PolicyName, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(AppRoles.Admin)
            .AddRequirements(new ActiveAdminRequirement()));
        services.AddScoped<IAuthorizationHandler, ActiveAdminHandler>();
        return services;
    }
}

/// <summary>
/// Роль у токені може бути застарілою до 15 хв, тож для адмін-дій перевіряємо БД: користувач існує,
/// лишається адміном і не заблокований.
/// </summary>
public sealed class ActiveAdminRequirement : IAuthorizationRequirement;

public sealed class ActiveAdminHandler(UserManager<IdentityUser> users) : AuthorizationHandler<ActiveAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveAdminRequirement requirement)
    {
        var id = context.User.Identity?.Name;
        var user = id is null ? null : await users.FindByIdAsync(id);
        if (user is not null && await users.IsInRoleAsync(user, AppRoles.Admin) && !await users.IsLockedOutAsync(user))
        {
            context.Succeed(requirement);
        }
    }
}
