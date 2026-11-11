using LocalMind.Server.Data;
using LocalMind.Server.Endpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var connectionString = builder.Configuration.GetConnectionString("Server")
    ?? throw new InvalidOperationException("Не задано рядок підключення ConnectionStrings:Server.");
builder.Services.AddDbContext<ServerDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddAuthentication();
builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        // Довжина замість правил складу (NIST SP 800-63B, DR-016).
        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddSignInManager()
    .AddEntityFrameworkStores<ServerDbContext>();

var app = builder.Build();

// Сервер — один процес з однією SQLite-базою, тож міграції застосовуємо на старті.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<ServerDbContext>().Database.MigrateAsync();
}

// Будь-який необроблений виняток → ProblemDetails 500 без деталей винятку (і в Development теж).
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "LocalMind API v1"));
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

var api = app.MapGroup("/api/v1");
api.MapHealthChecks("/health").WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));
api.MapAuthEndpoints();

await app.RunAsync();
