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
public sealed record RoleResponse(Guid Id, string Code, string Name, string? Description, RoleStatus Status, bool IsSystem, DateTimeOffset CreatedAt, DateTimeOffset? ModifiedAt);
public sealed record CreateRoleRequest(string Code, string Name, string? Description);
public sealed record RenameRoleRequest(string Name);
public sealed record PermissionResponse(Guid Id, string Code, string Name, string Module, string? Description);

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

public interface IRolePermissionStore
{
    Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken cancellationToken);
    Task<Role?> GetRoleAsync(RoleId id, CancellationToken cancellationToken);
    Task<Role?> GetRoleByCodeAsync(string code, CancellationToken cancellationToken);
    Task<bool> RoleCodeExistsAsync(string code, CancellationToken cancellationToken);
    Task AddRoleAsync(Role role, CancellationToken cancellationToken);
    Task<IReadOnlyList<Permission>> ListPermissionsAsync(CancellationToken cancellationToken);
    Task<Permission?> GetPermissionByCodeAsync(string code, CancellationToken cancellationToken);
    Task<IReadOnlyList<Permission>> ListRolePermissionsAsync(RoleId roleId, CancellationToken cancellationToken);
    Task<bool> HasRolePermissionAsync(RoleId roleId, PermissionId permissionId, CancellationToken cancellationToken);
    Task AddRolePermissionAsync(RoleId roleId, PermissionId permissionId, CancellationToken cancellationToken);
    Task RemoveRolePermissionAsync(RoleId roleId, PermissionId permissionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Role>> ListUserRolesAsync(UserId userId, CancellationToken cancellationToken);
    Task<bool> HasUserRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken);
    Task AddUserRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken);
    Task RemoveUserRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken);
    Task<bool> UserHasPermissionAsync(UserId userId, string permissionCode, CancellationToken cancellationToken);
    Task EnsurePermissionCatalogueAsync(CancellationToken cancellationToken);
    Task EnsureSystemAdministratorAsync(UserId userId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IRolePermissionService
{
    Task<RoleResponse> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken);
    Task<RoleResponse?> GetRoleAsync(RoleId id, CancellationToken cancellationToken);
    Task<RoleResponse?> RenameRoleAsync(RoleId id, RenameRoleRequest request, CancellationToken cancellationToken);
    Task<RoleResponse?> SetRoleStatusAsync(RoleId id, bool active, CancellationToken cancellationToken);
    Task<IReadOnlyList<PermissionResponse>> ListPermissionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PermissionResponse>?> ListRolePermissionsAsync(RoleId roleId, CancellationToken cancellationToken);
    Task<bool> AssignPermissionAsync(RoleId roleId, string permissionCode, CancellationToken cancellationToken);
    Task<bool> RemovePermissionAsync(RoleId roleId, string permissionCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoleResponse>?> ListUserRolesAsync(UserId userId, CancellationToken cancellationToken);
    Task<bool> AssignRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken);
    Task<bool> RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken);
}

public static class Permissions
{
    public static class Platform
    {
        public const string CompaniesRead = "platform.companies.read";
        public const string CompaniesManage = "platform.companies.manage";
        public const string UsersRead = "platform.users.read";
        public const string UsersManage = "platform.users.manage";
        public const string RolesRead = "platform.roles.read";
        public const string RolesManage = "platform.roles.manage";
        public const string PermissionsRead = "platform.permissions.read";
    }

    public static class Finance
    {
        public const string AccountsRead = "finance.accounts.read";
    }

    public static readonly IReadOnlyList<(string Code, string Name, string Module, string Description)> Catalogue =
    [
        (Platform.CompaniesRead, "Read companies", "platform", "View companies."),
        (Platform.CompaniesManage, "Manage companies", "platform", "Create and manage companies."),
        (Platform.UsersRead, "Read users", "platform", "View users."),
        (Platform.UsersManage, "Manage users", "platform", "Create and manage users and role assignments."),
        (Platform.RolesRead, "Read roles", "platform", "View roles."),
        (Platform.RolesManage, "Manage roles", "platform", "Create and manage roles and assignments."),
        (Platform.PermissionsRead, "Read permissions", "platform", "View the permission catalogue."),
        (Finance.AccountsRead, "Read finance accounts", "finance", "View finance accounts.")
    ];
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
        services.AddScoped<IRolePermissionService, RolePermissionService>();
        services.AddScoped<ActiveCompanyContext>();
        services.AddScoped<ICompanyContext>(provider => provider.GetRequiredService<ActiveCompanyContext>());
        return services;
    }
}
