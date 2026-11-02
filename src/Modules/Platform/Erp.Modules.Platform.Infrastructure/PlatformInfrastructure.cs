using Erp.Application.Abstractions;
using Erp.Modules.Platform.Application;
using Microsoft.Extensions.DependencyInjection;
using Erp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Platform.Infrastructure;

public static class PlatformInfrastructure
{
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PlatformDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "platform")));
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICompanyStore, PlatformCompanyStore>();
        services.AddScoped<IUserStore, PlatformUserStore>();
        services.AddScoped<IRolePermissionStore, RolePermissionStore>();
        services.AddScoped<IUserCompanyAccessStore, UserCompanyAccessStore>();
        services.AddScoped<IAuditEntryStore, AuditEntryStore>();
        services.AddScoped<INumberSequenceStore, NumberSequenceStore>();
        services.AddSingleton<IPasswordHasher<object>, PasswordHasher<object>>();
        services.AddSingleton<IPasswordService, AspNetPasswordService>();
        return services;
    }
}

internal sealed class AspNetPasswordService(IPasswordHasher<object> hasher) : IPasswordService
{
    private static readonly object UserMarker = new();

    public string Hash(string password) => hasher.HashPassword(UserMarker, password);

    public bool Verify(string passwordHash, string password) =>
        hasher.VerifyHashedPassword(UserMarker, passwordHash, password) == PasswordVerificationResult.Success;
}
