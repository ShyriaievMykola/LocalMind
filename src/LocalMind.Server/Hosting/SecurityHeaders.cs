namespace LocalMind.Server.Hosting;

/// <summary>
/// Захисні заголовки відповідей API (SEC-05, API8:2023). Відповіді містять токени й шифротекст бекапів,
/// тому їх не можна кешувати проміжними вузлами, а браузер не повинен вгадувати тип вмісту.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var headers = ((HttpContext)state).Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                if (!headers.ContainsKey("Cache-Control"))
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            }, context);
            await next();
        });
    }
}
