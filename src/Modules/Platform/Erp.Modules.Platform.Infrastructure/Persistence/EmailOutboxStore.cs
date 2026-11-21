using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class EmailOutboxStore(PlatformDbContext db) : IEmailOutboxStore
{
    public Task AddRangeAsync(IEnumerable<EmailMessage> messages, CancellationToken ct)
    {
        db.EmailMessages.AddRange(messages);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<EmailMessage>> ClaimDueAsync(DateTimeOffset now, int batch, DateTimeOffset leaseUntil, Guid token, CancellationToken ct)
    {
        // One statement picks and reserves the rows; SKIP LOCKED makes concurrent workers take different ones.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE platform."EmailMessages" SET "LeaseToken" = {token}, "LeasedUntil" = {leaseUntil}
            WHERE "Id" IN (
                SELECT "Id" FROM platform."EmailMessages"
                WHERE "Status" = 'Pending' AND "NextAttemptAt" <= {now} AND ("LeasedUntil" IS NULL OR "LeasedUntil" < {now})
                ORDER BY "NextAttemptAt"
                LIMIT {batch}
                FOR UPDATE SKIP LOCKED)
            """, ct);
        return await db.EmailMessages.Where(x => x.LeaseToken == token).OrderBy(x => x.NextAttemptAt).ToListAsync(ct);
    }

    public async Task<int> PurgeAsync(DateTimeOffset sentBefore, DateTimeOffset failedBefore, CancellationToken ct) =>
        await db.EmailMessages.Where(x => x.Status == EmailStatus.Sent && x.SentAt < sentBefore).ExecuteDeleteAsync(ct)
        + await db.EmailMessages.Where(x => x.Status == EmailStatus.Failed && x.CreatedAt < failedBefore).ExecuteDeleteAsync(ct);

    public async Task<EmailOutboxStatus> StatusAsync(CompanyId companyId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = db.EmailMessages.AsNoTracking().Where(x => x.CompanyId == companyId);
        var since = now.AddHours(-24);
        var pending = await rows.CountAsync(x => x.Status == EmailStatus.Pending, ct);
        var failed = await rows.CountAsync(x => x.Status == EmailStatus.Failed, ct);
        var sent = await rows.CountAsync(x => x.Status == EmailStatus.Sent && x.SentAt >= since, ct);
        var lastError = await rows.Where(x => x.LastError != null).OrderByDescending(x => x.NextAttemptAt).Select(x => x.LastError).FirstOrDefaultAsync(ct);
        var lastSent = await rows.Where(x => x.SentAt != null).MaxAsync(x => x.SentAt, ct);
        var oldest = await rows.Where(x => x.Status == EmailStatus.Pending).MinAsync(x => (DateTimeOffset?)x.CreatedAt, ct);
        return new EmailOutboxStatus(pending, failed, sent, lastError, lastSent, oldest);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
