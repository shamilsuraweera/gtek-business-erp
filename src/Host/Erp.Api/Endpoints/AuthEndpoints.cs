using Erp.Api.Authentication;
using Erp.Application.Abstractions;
using Erp.Modules.Platform.Application;
using Erp.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication");
        group.MapPost("/login", async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>>(
            LoginRequest request, IUserService users, JwtTokenService tokens, CancellationToken ct) =>
        {
            var user = await users.AuthenticateAsync(request.UserName, request.Password, ct);
            return user is null
                ? TypedResults.Unauthorized()
                : TypedResults.Ok(new LoginResponse(tokens.CreateToken(user), "Bearer", DateTimeOffset.UtcNow.AddHours(1)));
        }).AllowAnonymous().WithSummary("Authenticate a user");
        group.MapGet("/me", async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult, NotFound>>(
            ICurrentUser currentUser, IUserService users, CancellationToken ct) =>
        {
            if (!currentUser.IsAuthenticated || currentUser.UserId is not UserId userId)
                return TypedResults.Unauthorized();
            var user = await users.GetByIdAsync(userId, ct);
            return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
        }).RequireAuthorization().WithSummary("Get the authenticated user");
        return endpoints;
    }

    private sealed record LoginRequest(string UserName, string Password);
}

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/users").WithTags("Users");
        group.MapPost("/", async Task<Results<Created<UserResponse>, UnauthorizedHttpResult>>(
            CreateUserRequest request, IUserService users, ICurrentUser currentUser, IRolePermissionStore permissions,
            HttpContext context, CancellationToken ct) =>
        {
            var isFirstUser = !await users.HasUsersAsync(ct);
            var isAdministrator = currentUser.IsAuthenticated && currentUser.UserId is { } userId &&
                await permissions.UserHasPermissionAsync(userId, Permissions.Platform.UsersManage, ct);
            var configuredSecret = context.RequestServices.GetRequiredService<IConfiguration>()["Authentication:BootstrapSecret"];
            var suppliedSecret = context.Request.Headers["X-Bootstrap-Secret"].SingleOrDefault();
            var isBootstrapRequest = isFirstUser && !string.IsNullOrWhiteSpace(configuredSecret) &&
                string.Equals(suppliedSecret, configuredSecret, StringComparison.Ordinal);
            if ((!isFirstUser && !isAdministrator) || (isFirstUser && !isBootstrapRequest && !isAdministrator))
                return TypedResults.Unauthorized();
            var user = await users.CreateAsync(request, isFirstUser, ct);
            return TypedResults.Created($"/api/v1/users/{user.Id}", user);
        }).AllowAnonymous().WithSummary("Create a user");
        group.MapGet("/", async (IUserService users, CancellationToken ct) =>
            TypedResults.Ok(await users.ListAsync(ct))).RequirePermission(Permissions.Platform.UsersRead);
        group.MapGet("/{id:guid}", async Task<Results<Ok<UserResponse>, NotFound>>(
            Guid id, IUserService users, CancellationToken ct) =>
        {
            var user = await users.GetByIdAsync(new UserId(id), ct);
            return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
        }).RequirePermission(Permissions.Platform.UsersRead);
        group.MapGet("/by-username/{userName}", async Task<Results<Ok<UserResponse>, NotFound>>(
            string userName, IUserService users, CancellationToken ct) =>
        {
            var user = await users.GetByUserNameAsync(userName, ct);
            return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
        }).RequirePermission(Permissions.Platform.UsersRead);
        group.MapPost("/{id:guid}/activate", ChangeStatus(true)).RequirePermission(Permissions.Platform.UsersManage);
        group.MapPost("/{id:guid}/deactivate", ChangeStatus(false)).RequirePermission(Permissions.Platform.UsersManage);
        group.MapGet("/{userId:guid}/roles", async (Guid userId, IRolePermissionService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListUserRolesAsync(new UserId(userId), ct))).RequirePermission(Permissions.Platform.UsersRead);
        group.MapPost("/{userId:guid}/roles/{roleId:guid}", async Task<Results<Ok, NotFound>>(
            Guid userId, Guid roleId, IRolePermissionService service, CancellationToken ct) =>
            await service.AssignRoleAsync(new UserId(userId), new RoleId(roleId), ct)
                ? TypedResults.Ok() : TypedResults.NotFound()).RequirePermission(Permissions.Platform.UsersManage);
        group.MapDelete("/{userId:guid}/roles/{roleId:guid}", async Task<Results<NoContent, NotFound>>(
            Guid userId, Guid roleId, IRolePermissionService service, CancellationToken ct) =>
            await service.RemoveRoleAsync(new UserId(userId), new RoleId(roleId), ct)
                ? TypedResults.NoContent() : TypedResults.NotFound()).RequirePermission(Permissions.Platform.UsersManage);
        return endpoints;
    }

    private static Delegate ChangeStatus(bool activate) =>
        async Task<Results<Ok<UserResponse>, NotFound>>(Guid id, IUserService users, CancellationToken ct) =>
        {
            var user = activate ? await users.ActivateAsync(new UserId(id), ct) : await users.DeactivateAsync(new UserId(id), ct);
            return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
        };
}

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/roles").WithTags("Roles");
        group.MapGet("/", async (IRolePermissionService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListRolesAsync(ct))).RequirePermission(Permissions.Platform.RolesRead);
        group.MapPost("/", async (CreateRoleRequest request, IRolePermissionService service, CancellationToken ct) =>
        {
            var role = await service.CreateRoleAsync(request, ct);
            return TypedResults.Created($"/api/v1/roles/{role.Id}", role);
        }).RequirePermission(Permissions.Platform.RolesManage);
        group.MapGet("/{id:guid}", async Task<Results<Ok<RoleResponse>, NotFound>>(
            Guid id, IRolePermissionService service, CancellationToken ct) =>
        {
            var role = await service.GetRoleAsync(new RoleId(id), ct);
            return role is null ? TypedResults.NotFound() : TypedResults.Ok(role);
        }).RequirePermission(Permissions.Platform.RolesRead);
        group.MapPut("/{id:guid}/name", async Task<Results<Ok<RoleResponse>, NotFound>>(
            Guid id, RenameRoleRequest request, IRolePermissionService service, CancellationToken ct) =>
        {
            var role = await service.RenameRoleAsync(new RoleId(id), request, ct);
            return role is null ? TypedResults.NotFound() : TypedResults.Ok(role);
        }).RequirePermission(Permissions.Platform.RolesManage);
        group.MapPost("/{id:guid}/activate", (Guid id, IRolePermissionService service, CancellationToken ct) =>
            service.SetRoleStatusAsync(new RoleId(id), true, ct)).RequirePermission(Permissions.Platform.RolesManage);
        group.MapPost("/{id:guid}/deactivate", (Guid id, IRolePermissionService service, CancellationToken ct) =>
            service.SetRoleStatusAsync(new RoleId(id), false, ct)).RequirePermission(Permissions.Platform.RolesManage);
        group.MapGet("/{id:guid}/permissions", async (Guid id, IRolePermissionService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListRolePermissionsAsync(new RoleId(id), ct))).RequirePermission(Permissions.Platform.RolesRead);
        group.MapPost("/{id:guid}/permissions/{permissionCode}", async Task<Results<Ok, NotFound, Conflict>>(
            Guid id, string permissionCode, IRolePermissionService service, CancellationToken ct) =>
        {
            try { return await service.AssignPermissionAsync(new RoleId(id), permissionCode, ct) ? TypedResults.Ok() : TypedResults.NotFound(); }
            catch (InvalidOperationException) { return TypedResults.Conflict(); }
        }).RequirePermission(Permissions.Platform.RolesManage);
        group.MapDelete("/{id:guid}/permissions/{permissionCode}", async Task<Results<NoContent, NotFound>>(
            Guid id, string permissionCode, IRolePermissionService service, CancellationToken ct) =>
            await service.RemovePermissionAsync(new RoleId(id), permissionCode, ct)
                ? TypedResults.NoContent() : TypedResults.NotFound()).RequirePermission(Permissions.Platform.RolesManage);
        return endpoints;
    }
}

public static class PermissionEndpoints
{
    public static IEndpointRouteBuilder MapPermissionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/permissions", async (IRolePermissionService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListPermissionsAsync(ct))).WithTags("Permissions")
            .RequirePermission(Permissions.Platform.PermissionsRead);
        return endpoints;
    }
}
