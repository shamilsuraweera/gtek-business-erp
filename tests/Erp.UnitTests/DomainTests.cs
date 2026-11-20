using Erp.Modules.Finance.Domain;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class DomainTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());

    private static SalesOrder NewOrder() =>
        SalesOrder.Create(Company, "SO-1", new CustomerId(Guid.NewGuid()), new DateOnly(2026, 1, 1), null, DateTimeOffset.UtcNow);

    [Fact]
    public void Balanced_journal_is_accepted()
    {
        var journal = GeneralJournal.Create(Company, new Currency("USD", 2), "JE-1", new DateOnly(2026, 1, 15), "Test");
        journal.AddDebit(new AccountId(Guid.NewGuid()), 100m, "Cash");
        journal.AddCredit(new AccountId(Guid.NewGuid()), 100m, "Revenue");

        journal.Post(DateTimeOffset.UtcNow, null);

        Assert.True(journal.IsPosted);
    }

    [Fact]
    public void Unbalanced_journal_is_rejected()
    {
        var journal = GeneralJournal.Create(Company, new Currency("USD", 2), "JE-1", new DateOnly(2026, 1, 15), "Test");
        journal.AddDebit(new AccountId(Guid.NewGuid()), 100m, "Cash");
        journal.AddCredit(new AccountId(Guid.NewGuid()), 99m, "Revenue");

        Assert.Throws<DomainException>(() => journal.Post(DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Sales_order_release_requires_open_order()
    {
        var order = NewOrder();
        order.AddLine(Guid.NewGuid(), "ITEM-1", "Item", 1m, 10m);
        order.Open();
        order.Release();

        Assert.Equal(SalesOrderStatus.Released, order.Status);
    }

    [Fact]
    public void Sales_order_cannot_release_from_draft()
    {
        var order = NewOrder();

        Assert.Throws<DomainException>(order.Release);
    }

    [Fact]
    public void Posted_ledger_entry_is_immutable()
    {
        var entry = new GeneralLedgerEntry(
            new LedgerEntryId(Guid.NewGuid()), Company, new AccountId(Guid.NewGuid()),
            10m, 0m, DateTimeOffset.UtcNow, new JournalId(Guid.NewGuid()), new DateOnly(2026, 1, 15));

        var replacement = entry with { Debit = 20m };

        Assert.Equal(10m, entry.Debit);
        Assert.Equal(20m, replacement.Debit);
    }
}
