using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Application;

public sealed class AuditTrailService(
    IAuditEntryStore store,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IUserStore users,
    IClock clock,
    IAuditRequestContext requestContext) : IAuditTrail
{
    public async Task RecordAsync(
        string category,
        string action,
        string entityType,
        string? entityId,
        string? entityDisplay,
        AuditOutcome outcome,
        IReadOnlyDictionary<string, object?>? metadata,
        CancellationToken cancellationToken)
    {
        UserId? actorId = currentUser.UserId;
        string? actorName = null;
        if (actorId is { } id)
            actorName = (await users.GetByIdAsync(id, cancellationToken))?.UserName;

        var entry = AuditEntry.Create(
            clock.UtcNow,
            actorId,
            actorName,
            companyContext.HasCompany ? companyContext.CompanyId : null,
            category,
            action,
            entityType,
            entityId,
            entityDisplay,
            outcome,
            requestContext.CorrelationId,
            requestContext.IpAddress,
            metadata is null ? null : JsonSerializer.Serialize(metadata));

        await store.AddAsync(entry, cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        store.SaveChangesAsync(cancellationToken);

    public async Task<AuditPageResponse> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        var result = await store.QueryAsync(query, cancellationToken);
        return new AuditPageResponse(
            result.Items.Select(entry => new AuditEntryResponse(
                entry.Id,
                entry.OccurredAt,
                entry.ActorUserId?.Value,
                entry.ActorUserName,
                entry.CompanyId?.Value,
                entry.Category,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.EntityDisplay,
                entry.Outcome,
                entry.CorrelationId,
                entry.IpAddress,
                entry.MetadataJson)).ToArray(),
            query.Page,
            query.PageSize,
            result.TotalCount);
    }
}
