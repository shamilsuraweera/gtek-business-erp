using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class RolePermissionStore(PlatformDbContext db) : IRolePermissionStore
{
    public Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct) => db.Roles.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct).ContinueWith(t => (IReadOnlyList<Role>)t.Result, ct);
    public Task<Role?> GetRoleAsync(RoleId id, CancellationToken ct) => db.Roles.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Role?> GetRoleByCodeAsync(string code, CancellationToken ct) => db.Roles.SingleOrDefaultAsync(x => x.Code == code, ct);
    public Task<bool> RoleCodeExistsAsync(string code, CancellationToken ct) => db.Roles.AnyAsync(x => x.Code == code, ct);
    public Task AddRoleAsync(Role role, CancellationToken ct) { db.Roles.Add(role); return Task.CompletedTask; }
    public Task<IReadOnlyList<Permission>> ListPermissionsAsync(CancellationToken ct) => db.Permissions.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct).ContinueWith(t => (IReadOnlyList<Permission>)t.Result, ct);
    public Task<Permission?> GetPermissionByCodeAsync(string code, CancellationToken ct) => db.Permissions.SingleOrDefaultAsync(x => x.Code == code, ct);
    public Task<IReadOnlyList<Permission>> ListRolePermissionsAsync(RoleId roleId, CancellationToken ct) =>
        db.RolePermissions.Where(x => x.RoleId == roleId).Join(db.Permissions, x => x.PermissionId, x => x.Id, (_, p) => p).OrderBy(x => x.Code).ToListAsync(ct).ContinueWith(t => (IReadOnlyList<Permission>)t.Result, ct);
    public Task<bool> HasRolePermissionAsync(RoleId roleId, PermissionId permissionId, CancellationToken ct) => db.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == permissionId, ct);
    public Task AddRolePermissionAsync(RoleId roleId, PermissionId permissionId, CancellationToken ct) { db.RolePermissions.Add(new RolePermissionEntity(roleId, permissionId)); return Task.CompletedTask; }
    public async Task RemoveRolePermissionAsync(RoleId roleId, PermissionId permissionId, CancellationToken ct) { var row = await db.RolePermissions.SingleOrDefaultAsync(x => x.RoleId == roleId && x.PermissionId == permissionId, ct); if (row is not null) db.RolePermissions.Remove(row); }
    public Task<IReadOnlyList<Role>> ListUserRolesAsync(UserId userId, CancellationToken ct) =>
        db.UserRoles.Where(x => x.UserId == userId).Join(db.Roles, x => x.RoleId, x => x.Id, (_, r) => r).OrderBy(x => x.Code).ToListAsync(ct).ContinueWith(t => (IReadOnlyList<Role>)t.Result, ct);
    public Task<bool> HasUserRoleAsync(UserId userId, RoleId roleId, CancellationToken ct) => db.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == roleId, ct);
    public Task AddUserRoleAsync(UserId userId, RoleId roleId, CancellationToken ct) { db.UserRoles.Add(new UserRoleEntity(userId, roleId)); return Task.CompletedTask; }
    public async Task RemoveUserRoleAsync(UserId userId, RoleId roleId, CancellationToken ct) { var row = await db.UserRoles.SingleOrDefaultAsync(x => x.UserId == userId && x.RoleId == roleId, ct); if (row is not null) db.UserRoles.Remove(row); }
    public Task<bool> UserHasPermissionAsync(UserId userId, string code, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId && u.Status == UserStatus.Active)
            .Join(db.UserRoles, u => u.Id, ur => ur.UserId, (_, ur) => ur)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r)
            .Where(r => r.Status == RoleStatus.Active)
            .Join(db.RolePermissions, r => r.Id, rp => rp.RoleId, (_, rp) => rp)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (_, p) => p)
            .AnyAsync(p => p.Code == code, ct);

    public async Task EnsurePermissionCatalogueAsync(CancellationToken ct)
    {
        var existing = await db.Permissions.ToDictionaryAsync(x => x.Code, ct);
        foreach (var item in Permissions.Catalogue)
            if (!existing.ContainsKey(item.Code))
                db.Permissions.Add(Permission.Create(item.Code, item.Name, item.Module, item.Description, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureSystemAdministratorAsync(UserId userId, CancellationToken ct)
    {
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Code == "SYSTEM_ADMIN", ct);
        if (role is null)
        {
            role = Role.Create("SYSTEM_ADMIN", "System Administrator", "Initial installation administrator.", true, DateTimeOffset.UtcNow);
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
        }
        var permissionIds = await db.Permissions.Select(x => x.Id).ToListAsync(ct);
        foreach (var permissionId in permissionIds)
            if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permissionId, ct))
                db.RolePermissions.Add(new RolePermissionEntity(role.Id, permissionId));
        if (!await db.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == role.Id, ct))
            db.UserRoles.Add(new UserRoleEntity(userId, role.Id));
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class UserRoleEntity
{
    private UserRoleEntity() { }
    public UserRoleEntity(UserId userId, RoleId roleId) => (UserId, RoleId) = (userId, roleId);
    public UserId UserId { get; private set; }
    public RoleId RoleId { get; private set; }
}

public sealed class RolePermissionEntity
{
    private RolePermissionEntity() { }
    public RolePermissionEntity(RoleId roleId, PermissionId permissionId) => (RoleId, PermissionId) = (roleId, permissionId);
    public RoleId RoleId { get; private set; }
    public PermissionId PermissionId { get; private set; }
}
