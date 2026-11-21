using Erp.Api.Authentication;
using Erp.Modules.Platform.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class NotificationEndpoints
{
    /// <summary>A user's own notifications for the active company; any signed-in user may read and mark their own.</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireCompanyContext().RequireAuthorization();
        group.MapGet("/", async (bool? unread, int? take, INotificationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(unread ?? false, take ?? 20, ct)));
        group.MapGet("/unread-count", async (INotificationService service, CancellationToken ct) =>
            TypedResults.Ok(new UnreadCountResponse(await service.UnreadCountAsync(ct))));
        group.MapGet("/retention", async (INotificationService service, CancellationToken ct) => TypedResults.Ok(await service.GetRetentionAsync(ct)));
        group.MapPost("/{id:guid}/read", async Task<Results<Ok<NotificationResponse>, NotFound>>(Guid id, INotificationService service, CancellationToken ct) =>
        {
            var result = await service.MarkReadAsync(id, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        });
        group.MapPost("/read-all", async (INotificationService service, CancellationToken ct) =>
            TypedResults.Ok(new UnreadCountResponse(await service.MarkAllReadAsync(ct))));
        return endpoints;
    }
}

public static class NotificationSettingsEndpoints
{
    /// <summary>The active company's own notification retention (ADR 0025).</summary>
    public static IEndpointRouteBuilder MapNotificationSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notification-settings/retention").WithTags("Notifications").RequireCompanyContext();
        group.MapGet("/", async (INotificationRetentionSettings service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(ct)))
            .RequirePermission(Erp.Modules.Platform.Application.Permissions.Platform.NotificationRetentionRead);
        endpoints.MapGet("/api/v1/notification-settings/email", async (IEmailStatusService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetAsync(ct)))
            .WithTags("Notifications").RequireCompanyContext()
            .RequirePermission(Erp.Modules.Platform.Application.Permissions.Platform.NotificationRetentionRead);
        group.MapPut("/", async (UpdateNotificationRetentionRequest request, INotificationRetentionSettings service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAsync(request, ct)))
            .RequirePermission(Erp.Modules.Platform.Application.Permissions.Platform.NotificationRetentionManage);
        group.MapPut("/categories/{category}", async (string category, UpdateNotificationRetentionRequest request, INotificationRetentionSettings service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateCategoryAsync(category, request, ct)))
            .RequirePermission(Erp.Modules.Platform.Application.Permissions.Platform.NotificationRetentionManage);
        return endpoints;
    }
}
