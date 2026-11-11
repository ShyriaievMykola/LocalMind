var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

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
api.MapHealthChecks("/health");

app.Run();
