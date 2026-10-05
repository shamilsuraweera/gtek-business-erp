using Erp.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Sales.Infrastructure;

public static class SalesInfrastructure
{
    public static IServiceCollection AddSalesInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SalesDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
