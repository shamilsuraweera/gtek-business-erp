using Erp.Modules.Inventory.Application;
using Erp.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Inventory.Infrastructure;

public static class InventoryInfrastructure
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<InventoryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));
        services.AddScoped<IItemStore, ItemStore>();
        services.AddScoped<ILocationStore, LocationStore>();
        services.AddScoped<IStockStore, StockStore>();
        return services;
    }
}
