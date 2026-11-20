using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class AccountingPeriodStore(PlatformDbContext db) : IAccountingPeriodStore
{
    public Task AddAsync(AccountingPeriod period, CancellationToken cancellationToken)
    {
        db.AccountingPeriods.Add(period);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AccountingPeriod>> ListAsync(
        CompanyId companyId,
        CancellationToken cancellationToken) =>
        await db.AccountingPeriods.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.StartDate)
            .ToListAsync(cancellationToken);

    public Task<AccountingPeriod?> GetAsync(
        CompanyId companyId,
        AccountingPeriodId id,
        CancellationToken cancellationToken) =>
        db.AccountingPeriods.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == id,
            cancellationToken);

    public Task<AccountingPeriod?> GetByCodeAsync(
        CompanyId companyId,
        string code,
        CancellationToken cancellationToken)
    {
        var normalized = AccountingPeriod.NormalizeCode(code);
        return db.AccountingPeriods.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Code == normalized,
            cancellationToken);
    }

    public Task<bool> OverlapsAsync(
        CompanyId companyId,
        DateOnly startDate,
        DateOnly endDate,
        AccountingPeriodId? excludingId,
        CancellationToken cancellationToken) =>
        db.AccountingPeriods.AnyAsync(
            x => x.CompanyId == companyId &&
                 (!excludingId.HasValue || x.Id != excludingId.Value) &&
                 x.StartDate <= endDate &&
                 x.EndDate >= startDate,
            cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
