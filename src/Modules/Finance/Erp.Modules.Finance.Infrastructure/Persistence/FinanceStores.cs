using Erp.Modules.Finance.Application;
using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Finance.Infrastructure.Persistence;

public sealed class AccountStore(FinanceDbContext db) : IAccountStore
{
    public Task AddAsync(Account account, CancellationToken cancellationToken)
    {
        db.Accounts.Add(account);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Account>> ListAsync(CompanyId companyId, CancellationToken cancellationToken) =>
        await db.Accounts.Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(cancellationToken);

    public Task<Account?> GetAsync(CompanyId companyId, AccountId id, CancellationToken cancellationToken) =>
        db.Accounts.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task<Account?> GetByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken)
    {
        var normalized = Account.NormalizeCode(code);
        return db.Accounts.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Code == normalized, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class JournalStore(FinanceDbContext db) : IJournalStore
{
    public Task AddAsync(GeneralJournal journal, IReadOnlyList<GeneralLedgerEntry> entries, CancellationToken cancellationToken)
    {
        db.Journals.Add(journal);
        db.LedgerEntries.AddRange(entries);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<GeneralJournal>> ListAsync(CompanyId companyId, int take, CancellationToken cancellationToken) =>
        await db.Journals.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.JournalDate).ThenByDescending(x => x.Number)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<GeneralJournal?> GetAsync(CompanyId companyId, JournalId id, CancellationToken cancellationToken) =>
        db.Journals.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AccountBalance>> BalancesAsync(CompanyId companyId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var query = db.LedgerEntries.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (asOf is { } date) query = query.Where(x => x.PostingDate <= date);
        var rows = await query
            .GroupBy(x => x.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);
        return rows.Select(x => new AccountBalance(x.AccountId, x.Debit, x.Credit)).ToArray();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class AccountMappingStore(FinanceDbContext db) : IAccountMappingStore
{
    public async Task<IReadOnlyList<AccountMapping>> ListAsync(CompanyId companyId, CancellationToken cancellationToken) =>
        await db.AccountMappings.Where(x => x.CompanyId == companyId).ToListAsync(cancellationToken);

    public Task<AccountMapping?> GetAsync(CompanyId companyId, string role, CancellationToken cancellationToken) =>
        db.AccountMappings.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Role == role, cancellationToken);

    public Task AddAsync(AccountMapping mapping, CancellationToken cancellationToken)
    {
        db.AccountMappings.Add(mapping);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(AccountMapping mapping, CancellationToken cancellationToken)
    {
        db.AccountMappings.Remove(mapping);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class TaxCodeStore(FinanceDbContext db) : ITaxCodeStore
{
    public async Task<IReadOnlyList<TaxCode>> ListAsync(CompanyId companyId, CancellationToken cancellationToken) =>
        await db.TaxCodes.Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(cancellationToken);

    public Task<TaxCode?> GetAsync(CompanyId companyId, TaxCodeId id, CancellationToken cancellationToken) =>
        db.TaxCodes.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task<TaxCode?> GetByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken)
    {
        var normalized = TaxCode.NormalizeCode(code);
        return db.TaxCodes.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Code == normalized, cancellationToken);
    }

    public Task AddAsync(TaxCode taxCode, CancellationToken cancellationToken)
    {
        db.TaxCodes.Add(taxCode);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
