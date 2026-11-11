using LocalMind.Server.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocalMind.Server.Data;

/// <summary>База сервера (SQLite, DR-015): користувачі Identity, сесії, метадані бекапів.</summary>
public sealed class ServerDbContext(DbContextOptions<ServerDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    public DbSet<SnapshotEntity> Snapshots => Set<SnapshotEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ServerDbContext).Assembly);
    }
}

/// <summary>Створює контекст для <c>dotnet ef migrations</c>; у сервері не використовується.</summary>
public sealed class ServerDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ServerDbContext>
{
    /// <inheritdoc />
    public ServerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ServerDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
