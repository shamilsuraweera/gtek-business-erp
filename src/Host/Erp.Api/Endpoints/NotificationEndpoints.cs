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
        group.MapGet("/retention", (INotificationService service) => TypedResults.Ok(service.Retention));
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
