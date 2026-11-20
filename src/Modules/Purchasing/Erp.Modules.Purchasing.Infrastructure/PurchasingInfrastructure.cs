using Erp.Modules.Purchasing.Application;
using Erp.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Purchasing.Infrastructure;

public static class PurchasingInfrastructure
{
    public static IServiceCollection AddPurchasingInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PurchasingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "purchasing")));
        services.AddScoped<IPurchasingStore, PurchasingStore>();
        return services;
    }
}
