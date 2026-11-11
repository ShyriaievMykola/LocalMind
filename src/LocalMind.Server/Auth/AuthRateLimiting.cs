using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LocalMind.Server.Auth;

/// <summary>Ліміт запитів до <c>/api/v1/auth/*</c> з однієї IP-адреси (секція <c>RateLimiting</c>, DR-032).</summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Назва політики для <c>RequireRateLimiting</c>.</summary>
    public const string PolicyName = "auth";

    /// <summary>М'якша політика для refresh, logout і me: це не перебір паролів, а звичайна робота клієнта.</summary>
    public const string SessionPolicyName = "session";

    /// <summary>Скільки запитів до auth-ендпоінтів дозволено з однієї IP за вікно.</summary>
    [Range(1, 100_000)]
    public int AuthPermitLimit { get; set; } = 20;

    /// <summary>Скільки запитів refresh/logout/me дозволено з однієї IP за вікно.</summary>
    [Range(1, 100_000)]
    public int SessionPermitLimit { get; set; } = 60;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan AuthWindow { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Реєстрація rate limiting для auth-ендпоінтів.</summary>
public static class AuthRateLimiting
{
    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AuthRateLimitOptions>()
            .BindConfiguration(AuthRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<AuthRateLimitOptions>>((limiter, auth) =>
            {
                var limits = auth.Value;
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.AddPolicy(AuthRateLimitOptions.PolicyName, context =>
                    PerIp(context, limits.AuthPermitLimit, limits.AuthWindow));
                limiter.AddPolicy(AuthRateLimitOptions.SessionPolicyName, context =>
                    PerIp(context, limits.SessionPermitLimit, limits.AuthWindow));
                limiter.OnRejected = async (rejected, cancellationToken) =>
                {
                    if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        rejected.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    }

                    await TypedResults.Problem(
                            statusCode: StatusCodes.Status429TooManyRequests,
                            title: "Забагато спроб. Спробуйте трохи пізніше.")
                        .ExecuteAsync(rejected.HttpContext);
                };
            });
        return services;
    }

    // Лічильники в політик окремі, тож refresh і login не ділять одне вікно.
    private static RateLimitPartition<string> PerIp(HttpContext context, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 });
}
