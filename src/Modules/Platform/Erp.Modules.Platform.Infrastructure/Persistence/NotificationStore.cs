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

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
