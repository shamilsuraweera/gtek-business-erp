using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Erp.Modules.Purchasing.Infrastructure.Persistence;

public sealed class PurchasingDbContextFactory : IDesignTimeDbContextFactory<PurchasingDbContext>
{
    public PurchasingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ERP_DATABASE_CONNECTION")
            ?? throw new InvalidOperationException("Set ERP_DATABASE_CONNECTION when generating migrations.");
        var options = new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseNpgsql(
                new NpgsqlDataSourceBuilder(connectionString).Build(),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "purchasing"))
            .Options;
        return new PurchasingDbContext(options);
    }
}
