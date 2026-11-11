using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace LocalMind.Server.Tests;

/// <summary>Сервер у пам'яті з окремою in-memory SQLite-базою на кожен екземпляр фабрики.</summary>
public sealed class LocalMindServerFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=server-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;

    public LocalMindServerFactory()
    {
        // Спільна in-memory база живе, доки відкрите хоча б одне підключення.
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Server", _connectionString);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAlive.Dispose();
        }
    }
}
