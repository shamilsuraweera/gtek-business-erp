using Erp.Application.Abstractions;
using Erp.Modules.Platform.Application;
using Microsoft.Extensions.DependencyInjection;
using Erp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure;

public static class PlatformInfrastructure
{
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICompanyStore, PlatformCompanyStore>();
        return services;
    }
}
