using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Modules.Finance.Infrastructure.Persistence;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("Accounts", "finance");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new AccountId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
        b.Ignore(x => x.IsDebitNormal);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class GeneralJournalConfiguration : IEntityTypeConfiguration<GeneralJournal>
{
    public void Configure(EntityTypeBuilder<GeneralJournal> b)
    {
        b.ToTable("Journals", "finance");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new JournalId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.Source).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceReference).HasMaxLength(100);
        b.Property(x => x.PostedBy).HasConversion(
            x => x.HasValue ? x.Value.Value : (Guid?)null,
            x => x.HasValue ? new UserId(x.Value) : null);
        b.Property(x => x.ReversalOfJournalId).HasConversion(
            x => x.HasValue ? x.Value.Value : (Guid?)null,
            x => x.HasValue ? new JournalId(x.Value) : null);
        b.OwnsOne(x => x.Currency, c =>
        {
            c.Property(x => x.Code).HasColumnName("CurrencyCode").HasMaxLength(3).IsRequired();
            c.Property(x => x.DecimalPlaces).HasColumnName("CurrencyDecimalPlaces").IsRequired();
        });
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("JournalLines", "finance");
            l.WithOwner().HasForeignKey("JournalId");
            l.Property<int>("LineNo");
            l.HasKey("JournalId", "LineNo");
            l.Property(x => x.AccountId).HasConversion(x => x.Value, x => new AccountId(x)).IsRequired();
            l.Property(x => x.Debit).HasPrecision(19, 4);
            l.Property(x => x.Credit).HasPrecision(19, 4);
            l.Property(x => x.Description).HasMaxLength(500).IsRequired();
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.TotalDebit);
        b.Ignore(x => x.TotalCredit);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.JournalDate });
    }
}

internal sealed class GeneralLedgerEntryConfiguration : IEntityTypeConfiguration<GeneralLedgerEntry>
{
    public void Configure(EntityTypeBuilder<GeneralLedgerEntry> b)
    {
        b.ToTable("LedgerEntries", "finance");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new LedgerEntryId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.AccountId).HasConversion(x => x.Value, x => new AccountId(x)).IsRequired();
        b.Property(x => x.SourceJournalId).HasConversion(x => x.Value, x => new JournalId(x)).IsRequired();
        b.Property(x => x.Debit).HasPrecision(19, 4);
        b.Property(x => x.Credit).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.AccountId, x.PostingDate });
        b.HasIndex(x => x.SourceJournalId);
    }
}

internal sealed class AccountMappingConfiguration : IEntityTypeConfiguration<AccountMapping>
{
    public void Configure(EntityTypeBuilder<AccountMapping> b)
    {
        b.ToTable("AccountMappings", "finance");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new AccountMappingId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.AccountId).HasConversion(x => x.Value, x => new AccountId(x)).IsRequired();
        b.Property(x => x.Role).HasMaxLength(50).IsRequired();
        b.Property(x => x.ModifiedBy).HasConversion(
            x => x.HasValue ? x.Value.Value : (Guid?)null,
            x => x.HasValue ? new UserId(x.Value) : null);
        b.Ignore(x => x.DomainEvents);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.Role }).IsUnique();
    }
}

internal sealed class TaxCodeConfiguration : IEntityTypeConfiguration<TaxCode>
{
    public void Configure(EntityTypeBuilder<TaxCode> b)
    {
        b.ToTable("TaxCodes", "finance");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new TaxCodeId(x));
        b.Property(x => x.CompanyId).HasConversion(x => x.Value, x => new CompanyId(x)).IsRequired();
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.RatePercent).HasPrecision(7, 4);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}
