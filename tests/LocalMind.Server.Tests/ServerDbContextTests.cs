using LocalMind.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Server.Tests;

public sealed class ServerDbContextTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    [Fact]
    public async Task Migrations_MatchModel()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
