using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ERP_DATABASE_CONNECTION")
            ?? throw new InvalidOperationException("Set ERP_DATABASE_CONNECTION when generating migrations.");
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(
                new NpgsqlDataSourceBuilder(connectionString).Build(),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "platform"))
            .Options;
        return new PlatformDbContext(options);
    }
}
