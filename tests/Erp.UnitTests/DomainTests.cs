using Erp.Modules.Finance.Domain;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class DomainTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());

    [Fact]
    public void Balanced_journal_is_accepted()
    {
        var journal = GeneralJournal.Create(Company, new Currency("USD", 2));
        journal.AddDebit(new AccountId(Guid.NewGuid()), 100m, "Cash");
        journal.AddCredit(new AccountId(Guid.NewGuid()), 100m, "Revenue");

        journal.Post();

        Assert.True(journal.IsPosted);
    }

    [Fact]
    public void Unbalanced_journal_is_rejected()
    {
        var journal = GeneralJournal.Create(Company, new Currency("USD", 2));
        journal.AddDebit(new AccountId(Guid.NewGuid()), 100m, "Cash");
        journal.AddCredit(new AccountId(Guid.NewGuid()), 99m, "Revenue");

        Assert.Throws<DomainException>(() => journal.Post());
    }

    [Fact]
    public void Sales_order_release_requires_open_order()
    {
        var order = SalesOrder.Create(Company, new CustomerId(Guid.NewGuid()));
        order.AddLine("ITEM-1", 1m, 10m);
        order.Open();
        order.Release();

        Assert.Equal(SalesOrderStatus.Released, order.Status);
    }

    [Fact]
    public void Sales_order_cannot_release_from_draft()
    {
        var order = SalesOrder.Create(Company, new CustomerId(Guid.NewGuid()));

        Assert.Throws<DomainException>(order.Release);
    }

    [Fact]
    public void Posted_ledger_entry_is_immutable()
    {
        var entry = new GeneralLedgerEntry(
            new LedgerEntryId(Guid.NewGuid()), Company, new AccountId(Guid.NewGuid()),
            10m, 0m, DateTimeOffset.UtcNow, new JournalId(Guid.NewGuid()));

        var replacement = entry with { Debit = 20m };

        Assert.Equal(10m, entry.Debit);
        Assert.Equal(20m, replacement.Debit);
    }
}
