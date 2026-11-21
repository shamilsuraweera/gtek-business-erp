using Erp.Modules.Finance.Application;
using Erp.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Finance.Infrastructure;

public static class FinanceInfrastructure
{
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));
        services.AddScoped<IAccountStore, AccountStore>();
        services.AddScoped<IJournalStore, JournalStore>();
        services.AddScoped<IAccountMappingStore, AccountMappingStore>();
        services.AddScoped<ITaxCodeStore, TaxCodeStore>();
        return services;
    }
}
