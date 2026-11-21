using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class NotificationStore(PlatformDbContext db) : INotificationStore
{
    public Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct)
    {
        db.Notifications.AddRange(notifications);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Notification>> ListAsync(CompanyId companyId, UserId userId, bool unreadOnly, int take, CancellationToken ct)
    {
        var query = db.Notifications.AsNoTracking().Where(x => x.CompanyId == companyId && x.UserId == userId);
        if (unreadOnly) query = query.Where(x => x.ReadAt == null);
        return await query.OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(ct);
    }

    public Task<int> CountUnreadAsync(CompanyId companyId, UserId userId, CancellationToken ct) =>
        db.Notifications.CountAsync(x => x.CompanyId == companyId && x.UserId == userId && x.ReadAt == null, ct);

    public Task<Notification?> GetAsync(CompanyId companyId, UserId userId, Guid id, CancellationToken ct) =>
        db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId && x.UserId == userId, ct);

    public Task<int> MarkAllReadAsync(CompanyId companyId, UserId userId, DateTimeOffset at, CancellationToken ct) =>
        db.Notifications.Where(x => x.CompanyId == companyId && x.UserId == userId && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, at), ct);

    public Task<int> MarkSubjectReadAsync(CompanyId companyId, string subject, DateTimeOffset at, CancellationToken ct) =>
        db.Notifications.Where(x => x.CompanyId == companyId && x.Subject == subject && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, at), ct);

    public async Task<PurgeResult> PurgeAsync(PurgeScope scope, DateTimeOffset readBefore, DateTimeOffset? unreadBefore, int batchSize, CancellationToken ct)
    {
        IQueryable<Notification> inScope = db.Notifications;
        if (scope.Only is { } only) inScope = inScope.Where(x => x.CompanyId == only);
        if (scope.Except is { Count: > 0 } except) inScope = inScope.Where(x => !except.Contains(x.CompanyId));
        if (scope.Category is { } category) inScope = inScope.Where(x => x.Category == category);
        if (scope.ExceptCategories is { Count: > 0 } exceptCategories) inScope = inScope.Where(x => !exceptCategories.Contains(x.Category));

        var read = await DeleteInBatchesAsync(inScope.Where(x => x.ReadAt != null && x.ReadAt < readBefore).OrderBy(x => x.ReadAt), batchSize, ct);
        var unread = unreadBefore is { } cutoff
            ? await DeleteInBatchesAsync(inScope.Where(x => x.ReadAt == null && x.CreatedAt < cutoff).OrderBy(x => x.CreatedAt), batchSize, ct)
            : 0;
        return new PurgeResult(read, unread);
    }

    public Task<NotificationRetentionSetting?> GetRetentionSettingAsync(CompanyId companyId, string category, CancellationToken ct) =>
        db.NotificationRetentionSettings.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Category == category, ct);

    public async Task<IReadOnlyList<NotificationRetentionSetting>> ListRetentionSettingsAsync(CompanyId companyId, CancellationToken ct) =>
        await db.NotificationRetentionSettings.AsNoTracking().Where(x => x.CompanyId == companyId).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CompanyId companyId, CancellationToken ct) =>
        await db.Notifications.AsNoTracking().Where(x => x.CompanyId == companyId).Select(x => x.Category).Distinct().ToListAsync(ct);

    public async Task<IReadOnlyList<NotificationRetentionSetting>> ListRetentionSettingsAsync(CancellationToken ct) =>
        await db.NotificationRetentionSettings.AsNoTracking().ToListAsync(ct);

    public Task AddRetentionSettingAsync(NotificationRetentionSetting setting, CancellationToken ct)
    {
        db.NotificationRetentionSettings.Add(setting);
        return Task.CompletedTask;
    }

    public void RemoveRetentionSetting(NotificationRetentionSetting setting) => db.NotificationRetentionSettings.Remove(setting);

    private async Task<int> DeleteInBatchesAsync(IOrderedQueryable<Notification> due, int batchSize, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var ids = due.Select(x => x.Id).Take(batchSize);
            var deleted = await db.Notifications.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
            total += deleted;
            if (deleted < batchSize) return total;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
