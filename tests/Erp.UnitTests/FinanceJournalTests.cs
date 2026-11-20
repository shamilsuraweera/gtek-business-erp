using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class FinanceJournalTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());
    private static readonly AccountId Cash = new(Guid.NewGuid());
    private static readonly AccountId Revenue = new(Guid.NewGuid());

    private static GeneralJournal Draft(decimal debit = 100m, decimal credit = 100m)
    {
        var j = GeneralJournal.Create(Company, new Currency("LKR", 2), "JE-1", new DateOnly(2026, 3, 1), "Sale");
        j.AddDebit(Cash, debit, "Cash");
        j.AddCredit(Revenue, credit, "Revenue");
        return j;
    }

    [Fact]
    public void Posting_produces_one_ledger_entry_per_line_with_posting_date()
    {
        var journal = Draft();
        var entries = journal.Post(DateTimeOffset.UtcNow, null);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(new DateOnly(2026, 3, 1), e.PostingDate));
        Assert.Equal(100m, entries.Sum(e => e.Debit));
        Assert.Equal(100m, entries.Sum(e => e.Credit));
    }

    [Fact]
    public void Posted_journal_rejects_changes_and_double_posting()
    {
        var journal = Draft();
        journal.Post(DateTimeOffset.UtcNow, null);

        Assert.Throws<DomainException>(() => journal.AddDebit(Cash, 1m, "x"));
        Assert.Throws<DomainException>(() => journal.Post(DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Single_line_journal_is_rejected()
    {
        var journal = GeneralJournal.Create(Company, new Currency("LKR", 2), "JE-2", new DateOnly(2026, 3, 1), "x");
        journal.AddDebit(Cash, 5m, "x");
        Assert.Throws<DomainException>(() => journal.Post(DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Reversal_mirrors_lines_and_marks_original_reversed_once()
    {
        var original = Draft();
        original.Post(DateTimeOffset.UtcNow, null);

        var reversal = original.CreateReversal("JE-2", new DateOnly(2026, 3, 2), "undo");
        var entries = reversal.Post(DateTimeOffset.UtcNow, null);

        Assert.True(original.IsReversed);
        Assert.Equal(original.Id, reversal.ReversalOfJournalId);
        Assert.Equal(100m, entries.Single(e => e.AccountId == Cash).Credit);
        Assert.Equal(100m, entries.Single(e => e.AccountId == Revenue).Debit);
        Assert.Throws<DomainException>(() => original.CreateReversal("JE-3", new DateOnly(2026, 3, 2), "again"));
        Assert.Throws<DomainException>(() => reversal.CreateReversal("JE-4", new DateOnly(2026, 3, 2), "no"));
    }

    [Fact]
    public void Unposted_journal_cannot_be_reversed() =>
        Assert.Throws<DomainException>(() => Draft().CreateReversal("JE-2", new DateOnly(2026, 3, 2), "x"));

    [Fact]
    public void Account_codes_are_normalized_and_validated()
    {
        var account = Account.Create(Company, " ar-1000 ", "Receivables", AccountType.Asset);
        Assert.Equal("AR-1000", account.Code);
        Assert.True(account.IsDebitNormal);
        Assert.Throws<DomainException>(() => Account.Create(Company, " ", "x", AccountType.Asset));
    }
}
