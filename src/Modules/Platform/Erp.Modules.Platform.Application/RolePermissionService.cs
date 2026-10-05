using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Application;

public sealed class RolePermissionService(IRolePermissionStore store, IClock clock, IAuditTrail audit) : IRolePermissionService
{
    public async Task<RoleResponse> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var code = Role.NormalizeCode(request.Code);
        if (await store.RoleCodeExistsAsync(code, cancellationToken))
            throw new InvalidOperationException("A role with this code already exists.");
        var role = Role.Create(code, request.Name, request.Description, false, clock.UtcNow);
        await store.AddRoleAsync(role, cancellationToken);
        await audit.RecordAsync("RoleManagement", "role.created", "Role", role.Id.Value.ToString(), role.Code, AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["RoleCode"] = role.Code }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(role);
    }

    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken) =>
        (await store.ListRolesAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<RoleResponse?> GetRoleAsync(RoleId id, CancellationToken cancellationToken) =>
        MapNullable(await store.GetRoleAsync(id, cancellationToken));

    public async Task<RoleResponse?> RenameRoleAsync(RoleId id, RenameRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await store.GetRoleAsync(id, cancellationToken);
        if (role is null) return null;
        var previousName = role.Name;
        role.Rename(request.Name);
        await audit.RecordAsync("RoleManagement", "role.renamed", "Role", role.Id.Value.ToString(), role.Code, AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["PreviousName"] = previousName, ["NewName"] = role.Name }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(role);
    }

    public async Task<RoleResponse?> SetRoleStatusAsync(RoleId id, bool active, CancellationToken cancellationToken)
    {
        var role = await store.GetRoleAsync(id, cancellationToken);
        if (role is null) return null;
        var previousStatus = role.Status.ToString();
        if (active) role.Activate(); else role.Deactivate();
        var action = role.Status == RoleStatus.Active ? "role.activated" : "role.deactivated";
        await audit.RecordAsync("RoleManagement", action, "Role", role.Id.Value.ToString(), role.Code, AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["PreviousStatus"] = previousStatus, ["NewStatus"] = role.Status.ToString() }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(role);
    }

    public async Task<IReadOnlyList<PermissionResponse>> ListPermissionsAsync(CancellationToken cancellationToken) =>
        (await store.ListPermissionsAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<IReadOnlyList<PermissionResponse>?> ListRolePermissionsAsync(RoleId roleId, CancellationToken cancellationToken)
    {
        if (await store.GetRoleAsync(roleId, cancellationToken) is null) return null;
        return (await store.ListRolePermissionsAsync(roleId, cancellationToken)).Select(Map).ToArray();
    }

    public async Task<bool> AssignPermissionAsync(RoleId roleId, string permissionCode, CancellationToken cancellationToken)
    {
        var role = await store.GetRoleAsync(roleId, cancellationToken);
        var permission = await store.GetPermissionByCodeAsync(Permission.NormalizeCode(permissionCode), cancellationToken);
        if (role is null || permission is null || role.Status != RoleStatus.Active)
            return false;
        if (await store.HasRolePermissionAsync(roleId, permission.Id, cancellationToken))
            throw new InvalidOperationException("The permission is already assigned to this role.");
        await store.AddRolePermissionAsync(roleId, permission.Id, cancellationToken);
        await audit.RecordAsync("RoleManagement", "role.permission.granted", "Role", role.Id.Value.ToString(), role.Code, AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["RoleCode"] = role.Code, ["PermissionCode"] = permission.Code }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemovePermissionAsync(RoleId roleId, string permissionCode, CancellationToken cancellationToken)
    {
        var permission = await store.GetPermissionByCodeAsync(Permission.NormalizeCode(permissionCode), cancellationToken);
        if (await store.GetRoleAsync(roleId, cancellationToken) is null || permission is null)
            return false;
        await store.RemoveRolePermissionAsync(roleId, permission.Id, cancellationToken);
        var role = await store.GetRoleAsync(roleId, cancellationToken);
        await audit.RecordAsync("RoleManagement", "role.permission.revoked", "Role", roleId.Value.ToString(), role?.Code, AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["RoleCode"] = role?.Code, ["PermissionCode"] = permission.Code }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<RoleResponse>?> ListUserRolesAsync(UserId userId, CancellationToken cancellationToken)
    {
        var roles = await store.ListUserRolesAsync(userId, cancellationToken);
        return roles.Select(Map).ToArray();
    }

    public async Task<bool> AssignRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken)
    {
        var role = await store.GetRoleAsync(roleId, cancellationToken);
        if (role is null || role.Status != RoleStatus.Active || await store.HasUserRoleAsync(userId, roleId, cancellationToken))
            return false;
        await store.AddUserRoleAsync(userId, roleId, cancellationToken);
        await audit.RecordAsync("RoleManagement", "user.role.granted", "User", userId.Value.ToString(), userId.Value.ToString(), AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["TargetUserId"] = userId.Value, ["RoleCode"] = role.Code }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken)
    {
        if (await store.GetRoleAsync(roleId, cancellationToken) is null)
            return false;
        await store.RemoveUserRoleAsync(userId, roleId, cancellationToken);
        var role = await store.GetRoleAsync(roleId, cancellationToken);
        await audit.RecordAsync("RoleManagement", "user.role.revoked", "User", userId.Value.ToString(), userId.Value.ToString(), AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["TargetUserId"] = userId.Value, ["RoleCode"] = role?.Code }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static RoleResponse Map(Role role) => new(role.Id.Value, role.Code, role.Name, role.Description, role.Status, role.IsSystem, role.CreatedAt, role.ModifiedAt);
    private static RoleResponse? MapNullable(Role? role) => role is null ? null : Map(role);
    private static PermissionResponse Map(Permission permission) => new(permission.Id.Value, permission.Code, permission.Name, permission.Module, permission.Description);
}
