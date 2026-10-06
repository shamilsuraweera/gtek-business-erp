using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Erp.Api.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/audit", async Task<Results<Ok<AuditPageResponse>, BadRequest<string>>>(
            DateTimeOffset? from,
            DateTimeOffset? to,
            Guid? actorUserId,
            Guid? companyId,
            string? category,
            string? action,
            string? entityType,
            string? entityId,
            AuditOutcome? outcome,
            int? page,
            int? pageSize,
            IAuditTrail audit,
            CancellationToken cancellationToken) =>
        {
            var requestedPage = page ?? 1;
            var requestedPageSize = pageSize ?? 50;
            if (requestedPage < 1 || requestedPageSize < 1 || requestedPageSize > 200)
                return TypedResults.BadRequest("page must be at least 1 and pageSize must be between 1 and 200.");
            if (from is not null && to is not null && from > to)
                return TypedResults.BadRequest("from must not be later than to.");

            var query = new AuditQuery(from, to, actorUserId, companyId, category, action, entityType, entityId,
                outcome, requestedPage, requestedPageSize);
            return TypedResults.Ok(await audit.QueryAsync(query, cancellationToken));
        })
        .RequirePermission(Permissions.Platform.AuditRead)
        .WithTags("Audit")
        .WithSummary("Query the append-only audit history")
        .WithDescription("System-scoped audit query. Use companyId to filter history for one company.")
        .Produces<AuditPageResponse>(StatusCodes.Status200OK)
        .Produces<string>(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
