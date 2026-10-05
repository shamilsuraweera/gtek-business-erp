using Erp.Modules.Platform.Domain;
using Erp.Application.Abstractions;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Application;

public sealed class UserService(IUserStore store, IRolePermissionStore rolePermissions, IPasswordService passwords, IClock clock) : IUserService
{
    public async Task<UserResponse> CreateAsync(CreateUserRequest request, bool isBootstrapAdministrator, CancellationToken cancellationToken)
    {
        var userName = User.NormalizeUserName(request.UserName);
        var email = User.NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 12)
            throw new DomainException("Password must be at least 12 characters.");
        if (await store.ExistsByUserNameAsync(userName, cancellationToken))
            throw new InvalidOperationException("A user with this username already exists.");
        if (await store.ExistsByEmailAsync(email, cancellationToken))
            throw new InvalidOperationException("A user with this email already exists.");

        var user = User.Create(userName, email, clock.UtcNow, isBootstrapAdministrator);
        await store.AddAsync(user, passwords.Hash(request.Password), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        if (isBootstrapAdministrator)
        {
            await rolePermissions.EnsurePermissionCatalogueAsync(cancellationToken);
            await rolePermissions.EnsureSystemAdministratorAsync(user.Id, cancellationToken);
            await rolePermissions.SaveChangesAsync(cancellationToken);
        }
        return Map(user);
    }

    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<UserResponse?> GetByIdAsync(UserId id, CancellationToken cancellationToken) =>
        MapNullable(await store.GetByIdAsync(id, cancellationToken));

    public async Task<UserResponse?> GetByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        MapNullable(await store.GetByUserNameAsync(User.NormalizeUserName(userName), cancellationToken));

    public Task<UserResponse?> ActivateAsync(UserId id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, user => user.Activate(), cancellationToken);

    public Task<UserResponse?> DeactivateAsync(UserId id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, user => user.Deactivate(), cancellationToken);

    public Task<bool> HasUsersAsync(CancellationToken cancellationToken) => store.HasUsersAsync(cancellationToken);

    public async Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return null;

        string normalizedUserName;
        try
        {
            normalizedUserName = User.NormalizeUserName(userName);
        }
        catch (DomainException)
        {
            return null;
        }

        var user = await store.GetByUserNameAsync(normalizedUserName, cancellationToken);
        var hash = user is null ? null : await store.GetPasswordHashAsync(user.Id, cancellationToken);
        if (user is null || hash is null || user.Status != UserStatus.Active || !passwords.Verify(hash, password))
            return null;
        return new(user.Id, user.UserName, user.Email, user.IsBootstrapAdministrator);
    }

    private async Task<UserResponse?> ChangeStatusAsync(UserId id, Action<User> change, CancellationToken cancellationToken)
    {
        var user = await store.GetByIdAsync(id, cancellationToken);
        if (user is null) return null;
        change(user);
        await store.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    private static UserResponse Map(User user) =>
        new(user.Id.Value, user.UserName, user.Email, user.Status, user.CreatedAt, user.ModifiedAt);

    private static UserResponse? MapNullable(User? user) => user is null ? null : Map(user);
}
