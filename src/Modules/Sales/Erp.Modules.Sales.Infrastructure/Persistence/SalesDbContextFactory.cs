using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Erp.Modules.Sales.Infrastructure.Persistence;

public sealed class SalesDbContextFactory : IDesignTimeDbContextFactory<SalesDbContext>
{
    public SalesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ERP_DATABASE_CONNECTION")
            ?? throw new InvalidOperationException("Set ERP_DATABASE_CONNECTION when generating migrations.");
        var options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseNpgsql(
                new NpgsqlDataSourceBuilder(connectionString).Build(),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "sales"))
            .Options;
        return new SalesDbContext(options);
    }
}
