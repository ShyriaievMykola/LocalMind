using LocalMind.Server.Auth;
using LocalMind.Server.Data;
using LocalMind.Server.Endpoints;
using LocalMind.Server.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Кривий JSON → 400 і в Development (за замовчуванням там кидається виняток, який став би 500).
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

var connectionString = builder.Configuration.GetConnectionString("Server")
    ?? throw new InvalidOperationException("Не задано рядок підключення ConnectionStrings:Server.");
builder.Services.AddDbContext<ServerDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>, IHostEnvironment>((bearer, jwt, environment) =>
    {
        bearer.MapInboundClaims = false;

        // Причина відмови (прострочено, чужий ключ…) у WWW-Authenticate — лише для налагодження.
        bearer.IncludeErrorDetails = environment.IsDevelopment();
        bearer.TokenValidationParameters = JwtTokenService.CreateValidationParameters(jwt.Value);
    });
builder.Services.AddAuthorization();
builder.Services.AddOptions<BlobStoreOptions>()
    .BindConfiguration(BlobStoreOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<FileSystemBlobStore>();
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

        // Логін = email, а email перевіряє окремий валідатор; так проходять і кириличні адреси.
        options.User.AllowedUserNameCharacters = string.Empty;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddErrorDescriber<UkrainianIdentityErrorDescriber>()
    .AddPasswordValidator<EmailNotInPasswordValidator>()
    .AddSignInManager()
    .AddEntityFrameworkStores<ServerDbContext>();

var app = builder.Build();

// Без коректного ключа JWT сервер не стартує — перевіряємо до міграцій, а не лише в StartAsync.
_ = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

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
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api/v1");
api.MapHealthChecks("/health").WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));
api.MapAuthEndpoints();
api.MapBackupEndpoints();

await app.RunAsync();
