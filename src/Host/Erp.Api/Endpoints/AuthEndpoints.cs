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

        group.MapPost("/login", async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> (
            LoginRequest request, IUserService users, JwtTokenService tokens, CancellationToken cancellationToken) =>
        {
            var user = await users.AuthenticateAsync(request.UserName, request.Password, cancellationToken);
            if (user is null)
                return TypedResults.Unauthorized();
            var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
            return TypedResults.Ok(new LoginResponse(tokens.CreateToken(user), "Bearer", expiresAt));
        }).AllowAnonymous().WithSummary("Authenticate a user");

        group.MapGet("/me", async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult, NotFound>> (
            HttpContext httpContext, ICurrentUser currentUser, IUserService users, CancellationToken cancellationToken) =>
        {
            if (!currentUser.IsAuthenticated || currentUser.UserId is not UserId userId)
                return TypedResults.Unauthorized();
            var user = await users.GetByIdAsync(userId, cancellationToken);
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

        group.MapPost("/", async Task<Results<Created<UserResponse>, UnauthorizedHttpResult>> (
            CreateUserRequest request, IUserService users, ICurrentUser currentUser, HttpContext context, CancellationToken cancellationToken) =>
        {
            var isFirstUser = !await users.HasUsersAsync(cancellationToken);
            var isAdministrator = currentUser.IsAuthenticated &&
                context.User.HasClaim("bootstrap_admin", "true");
            var isBootstrapRequest = context.Request.Headers.TryGetValue("X-Bootstrap-Secret", out var suppliedSecret) &&
                !string.IsNullOrWhiteSpace(context.RequestServices.GetRequiredService<IConfiguration>()["Authentication:BootstrapSecret"]) &&
                string.Equals(suppliedSecret.SingleOrDefault(),
                    context.RequestServices.GetRequiredService<IConfiguration>()["Authentication:BootstrapSecret"],
                    StringComparison.Ordinal);
            if ((!isFirstUser && !isAdministrator) || (isFirstUser && !isBootstrapRequest && !isAdministrator))
                return TypedResults.Unauthorized();
            var user = await users.CreateAsync(request, isFirstUser, cancellationToken);
            return TypedResults.Created($"/api/v1/users/{user.Id}", user);
        }).AllowAnonymous().WithSummary("Create a user");

        group.MapGet("/", async (IUserService users, CancellationToken cancellationToken) =>
            TypedResults.Ok(await users.ListAsync(cancellationToken))).RequireAuthorization("PlatformAdministrator").WithSummary("List users");

        group.MapGet("/{id:guid}", async Task<Results<Ok<UserResponse>, NotFound>> (
            Guid id, IUserService users, CancellationToken cancellationToken) =>
        {
            var user = await users.GetByIdAsync(new UserId(id), cancellationToken);
            return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
        }).RequireAuthorization("PlatformAdministrator").WithSummary("Get a user");

        group.MapGet("/by-username/{userName}", async Task<Results<Ok<UserResponse>, NotFound>> (
            string userName, IUserService users, CancellationToken cancellationToken) =>
        {
            var user = await users.GetByUserNameAsync(userName, cancellationToken);
            return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
        }).RequireAuthorization("PlatformAdministrator").WithSummary("Get a user by username");

        group.MapPost("/{id:guid}/activate", ChangeStatus(true)).RequireAuthorization("PlatformAdministrator").WithSummary("Activate a user");
        group.MapPost("/{id:guid}/deactivate", ChangeStatus(false)).RequireAuthorization("PlatformAdministrator").WithSummary("Deactivate a user");
        return endpoints;
    }

    private static Delegate ChangeStatus(bool activate) =>
        async Task<Results<Ok<UserResponse>, NotFound>> (Guid id, IUserService users, CancellationToken cancellationToken) =>
        {
            var user = activate
                ? await users.ActivateAsync(new UserId(id), cancellationToken)
                : await users.DeactivateAsync(new UserId(id), cancellationToken);
            return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
        };
}
