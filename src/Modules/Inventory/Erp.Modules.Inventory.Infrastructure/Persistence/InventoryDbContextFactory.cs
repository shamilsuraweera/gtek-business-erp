using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Erp.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ERP_DATABASE_CONNECTION")
            ?? throw new InvalidOperationException("Set ERP_DATABASE_CONNECTION when generating migrations.");
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(
                new NpgsqlDataSourceBuilder(connectionString).Build(),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
            .Options;
        return new InventoryDbContext(options);
    }
}
