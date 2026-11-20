using Erp.Modules.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountMapping> AccountMappings => Set<AccountMapping>();
    public DbSet<GeneralJournal> Journals => Set<GeneralJournal>();
    public DbSet<GeneralLedgerEntry> LedgerEntries => Set<GeneralLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("finance");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);
    }
}
