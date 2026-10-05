using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Platform.Application;

public sealed record CompanyResponse(
    Guid Id,
    string Code,
    string Name,
    CompanyStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

public sealed record CreateCompanyRequest(string Code, string Name);
public sealed record RenameCompanyRequest(string Name);
public sealed record UserResponse(Guid Id, string UserName, string Email, UserStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? ModifiedAt);
public sealed record CreateUserRequest(string UserName, string Email, string Password);

public interface ICompanyService
{
    Task<CompanyResponse> CreateAsync(CreateCompanyRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyResponse>> ListAsync(CancellationToken cancellationToken);
    Task<CompanyResponse?> GetByIdAsync(CompanyId id, CancellationToken cancellationToken);
    Task<CompanyResponse?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<CompanyResponse?> RenameAsync(CompanyId id, RenameCompanyRequest request, CancellationToken cancellationToken);
    Task<CompanyResponse?> ActivateAsync(CompanyId id, CancellationToken cancellationToken);
    Task<CompanyResponse?> DeactivateAsync(CompanyId id, CancellationToken cancellationToken);
}

public interface ICompanyStore
{
    Task AddAsync(Company company, CancellationToken cancellationToken);
    Task<IReadOnlyList<Company>> ListAsync(CancellationToken cancellationToken);
    Task<Company?> GetByIdAsync(CompanyId id, CancellationToken cancellationToken);
    Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IUserService
{
    Task<UserResponse> CreateAsync(CreateUserRequest request, bool isBootstrapAdministrator, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken cancellationToken);
    Task<UserResponse?> GetByIdAsync(UserId id, CancellationToken cancellationToken);
    Task<UserResponse?> GetByUserNameAsync(string userName, CancellationToken cancellationToken);
    Task<UserResponse?> ActivateAsync(UserId id, CancellationToken cancellationToken);
    Task<UserResponse?> DeactivateAsync(UserId id, CancellationToken cancellationToken);
    Task<bool> HasUsersAsync(CancellationToken cancellationToken);
    Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
}

public sealed record AuthenticatedUser(UserId Id, string UserName, string Email, bool IsBootstrapAdministrator);

public interface IUserStore
{
    Task AddAsync(User user, string passwordHash, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken);
    Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken);
    Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken);
    Task<bool> ExistsByUserNameAsync(string userName, CancellationToken cancellationToken);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken);
    Task<string?> GetPasswordHashAsync(UserId id, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<bool> HasUsersAsync(CancellationToken cancellationToken);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string passwordHash, string password);
}

public sealed class ActiveCompanyContext : ICompanyContext
{
    public CompanyId CompanyId { get; private set; }
    public bool HasCompany { get; private set; }

    public void Set(CompanyId companyId)
    {
        CompanyId = companyId;
        HasCompany = true;
    }
}

public sealed class CompanyService(ICompanyStore store, IClock clock) : ICompanyService
{
    public async Task<CompanyResponse> CreateAsync(CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        var code = Company.NormalizeCode(request.Code);
        if (await store.ExistsByCodeAsync(code, cancellationToken))
            throw new InvalidOperationException("A company with this code already exists.");

        var company = Company.Create(code, request.Name, clock.UtcNow);
        await store.AddAsync(company, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return MapRequired(company);
    }

    public async Task<IReadOnlyList<CompanyResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken)).Select(MapRequired).ToArray();

    public async Task<CompanyResponse?> GetByIdAsync(CompanyId id, CancellationToken cancellationToken) =>
        MapNullable(await store.GetByIdAsync(id, cancellationToken));

    public async Task<CompanyResponse?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        MapNullable(await store.GetByCodeAsync(Company.NormalizeCode(code), cancellationToken));

    public async Task<CompanyResponse?> RenameAsync(CompanyId id, RenameCompanyRequest request, CancellationToken cancellationToken)
    {
        var company = await store.GetByIdAsync(id, cancellationToken);
        if (company is null) return null;
        company.Rename(request.Name);
        await store.SaveChangesAsync(cancellationToken);
        return MapRequired(company);
    }

    public Task<CompanyResponse?> ActivateAsync(CompanyId id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, company => company.Activate(), cancellationToken);

    public Task<CompanyResponse?> DeactivateAsync(CompanyId id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, company => company.Deactivate(), cancellationToken);

    private async Task<CompanyResponse?> ChangeStatusAsync(CompanyId id, Action<Company> change, CancellationToken cancellationToken)
    {
        var company = await store.GetByIdAsync(id, cancellationToken);
        if (company is null) return null;
        change(company);
        await store.SaveChangesAsync(cancellationToken);
        return MapRequired(company);
    }

    private static CompanyResponse MapRequired(Company company) =>
        new(company.Id.Value, company.Code, company.Name, company.Status, company.CreatedAt, company.ModifiedAt);

    private static CompanyResponse? MapNullable(Company? company) =>
        company is null ? null : new(company.Id.Value, company.Code, company.Name, company.Status, company.CreatedAt, company.ModifiedAt);
}

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ActiveCompanyContext>();
        services.AddScoped<ICompanyContext>(provider => provider.GetRequiredService<ActiveCompanyContext>());
        return services;
    }
}
