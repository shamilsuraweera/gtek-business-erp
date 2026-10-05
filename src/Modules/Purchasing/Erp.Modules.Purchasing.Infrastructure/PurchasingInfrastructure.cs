using Erp.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Purchasing.Infrastructure;

public static class PurchasingInfrastructure
{
    public static IServiceCollection AddPurchasingInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PurchasingDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
