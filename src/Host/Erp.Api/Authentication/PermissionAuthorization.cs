using Erp.Application.Abstractions;
using Erp.Modules.Platform.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Erp.Api.Authentication;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionAuthorizationHandler(ICurrentUser currentUser, IRolePermissionStore store)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (currentUser.IsAuthenticated && currentUser.UserId is { } userId &&
            await store.UserHasPermissionAsync(
                userId,
                requirement.Permission,
                (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None))
            context.Succeed(requirement);
    }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "Permission:";

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
            return base.GetPolicyAsync(policyName);
        var permission = policyName[Prefix.Length..];
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.Prefix + permission);
}
