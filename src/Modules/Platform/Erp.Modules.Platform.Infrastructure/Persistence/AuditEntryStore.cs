using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class AuditEntryStore(PlatformDbContext dbContext) : IAuditEntryStore
{
    public Task AddAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        dbContext.Set<AuditEntry>().Add(entry);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<(IReadOnlyList<AuditEntry> Items, int TotalCount)> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken)
    {
        var entries = dbContext.Set<AuditEntry>().AsNoTracking().AsQueryable();
        if (query.From is { } from) entries = entries.Where(x => x.OccurredAt >= from);
        if (query.To is { } to) entries = entries.Where(x => x.OccurredAt <= to);
        if (query.ActorUserId is { } actor) entries = entries.Where(x => x.ActorUserId != null && x.ActorUserId.Value.Value == actor);
        if (query.CompanyId is { } company) entries = entries.Where(x => x.CompanyId != null && x.CompanyId.Value.Value == company);
        if (!string.IsNullOrWhiteSpace(query.Category)) entries = entries.Where(x => x.Category == query.Category);
        if (!string.IsNullOrWhiteSpace(query.Action)) entries = entries.Where(x => x.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.EntityType)) entries = entries.Where(x => x.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityId)) entries = entries.Where(x => x.EntityId == query.EntityId);
        if (query.Outcome is { } outcome) entries = entries.Where(x => x.Outcome == outcome);

        var total = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
