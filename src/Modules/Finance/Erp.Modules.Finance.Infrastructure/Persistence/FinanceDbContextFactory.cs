using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Erp.Modules.Finance.Infrastructure.Persistence;

public sealed class FinanceDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ERP_DATABASE_CONNECTION")
            ?? throw new InvalidOperationException("Set ERP_DATABASE_CONNECTION when generating migrations.");
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(
                new NpgsqlDataSourceBuilder(connectionString).Build(),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
            .Options;
        return new FinanceDbContext(options);
    }
}
